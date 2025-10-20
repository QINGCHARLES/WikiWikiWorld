#!/usr/bin/env python3
import argparse, os, socket, threading, select, sys, base64
from urllib.parse import urlsplit

def pump(a, b):
    try:
        sockets = [a, b]
        while True:
            r, _, _ = select.select(sockets, [], [], 60)
            if a in r:
                d = a.recv(65536)
                if not d: break
                b.sendall(d)
            if b in r:
                d = b.recv(65536)
                if not d: break
                a.sendall(d)
    finally:
        for s in (a,b):
            try: s.shutdown(socket.SHUT_RDWR)
            except: pass
            try: s.close()
            except: pass

def recv_until(sock, sep=b"\r\n\r\n", limit=65536):
    buf = b""
    while sep not in buf:
        chunk = sock.recv(4096)
        if not chunk: break
        buf += chunk
        if len(buf) > limit: break
    return buf

def mk_basic(user, pwd):
    raw = f"{user}:{pwd}".encode("utf-8")
    return "Basic " + base64.b64encode(raw).decode("ascii")

def first_line_status(hdr: bytes) -> bytes:
    return hdr.split(b"\r\n",1)[0] if hdr else b""

def try_connect_with_headers(corp, hostport, headers):
    req = (f"CONNECT {hostport} HTTP/1.1\r\n"
           f"Host: {hostport}\r\n" +
           "".join(h + "\r\n" for h in headers) +
           "Proxy-Connection: keep-alive\r\n"
           "Connection: keep-alive\r\n\r\n").encode("latin-1")
    corp.sendall(req)
    resp = recv_until(corp)
    return resp

def handle_client(client, thost, tport, bearer_raw, basic_hdr=None, also_auth_header=False):
    try:
        first = recv_until(client)
        if not first:
            client.close(); return

        # Normalize token (strip "jwt_" prefix if present)
        token = bearer_raw
        if token.startswith("jwt_"):
            token = token[4:]

        # Prepare auth headers we'll try (order matters)
        headers_sets = []

        # 1) Proxy-Authorization: Bearer <token>
        headers_sets.append([f"Proxy-Authorization: Bearer {token}"])

        # 2) Proxy-Authorization + Authorization (some proxies want Authorization even for CONNECT)
        if also_auth_header:
            headers_sets.append([f"Proxy-Authorization: Bearer {token}",
                                 f"Authorization: Bearer {token}"])
        # 3) Add Basic if provided (some proxies require Basic, ignore Bearer)
        if basic_hdr:
            headers_sets.append([f"Proxy-Authorization: {basic_hdr}"])
            if also_auth_header:
                headers_sets.append([f"Proxy-Authorization: {basic_hdr}",
                                     f"Authorization: {basic_hdr}"])

        # Open upstream connection to corp proxy
        hostport = f"{thost}:{tport}"
        corp = socket.create_connection((thost, tport), timeout=30)

        # Try header strategies until one returns 200
        ok = False
        last_status = b""
        for hs in headers_sets:
            resp = try_connect_with_headers(corp, hostport=first.split(b" ")[1].decode("latin-1"), headers=hs)
            status = first_line_status(resp)
            last_status = status
            if b" 200 " in status:
                # Tunnel established with this header set
                client.sendall(b"HTTP/1.1 200 Connection Established\r\n\r\n")
                pump(client, corp)
                ok = True
                break
            # Not OK; reopen socket and try next strategy
            try: corp.close()
            except: pass
            corp = socket.create_connection((thost, tport), timeout=30)

        if not ok:
            # Propagate last response (useful for debugging)
            client.sendall((last_status or b"HTTP/1.1 502 Bad Gateway") + b"\r\n\r\n")
            corp.close(); client.close()
            return

    except Exception:
        try: client.sendall(b"HTTP/1.1 500 Internal Server Error\r\n\r\n")
        except: pass
        try: client.close()
        except: pass

def serve(listen_host, listen_port, target_host, target_port, bearer, basic_hdr, also_auth_header):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((listen_host, listen_port))
    srv.listen(128)
    print(f"[bearer-proxy] listening on {listen_host}:{listen_port} -> proxy {target_host}:{target_port}", flush=True)
    while True:
        c, _ = srv.accept()
        threading.Thread(
            target=handle_client,
            args=(c, target_host, target_port, bearer, basic_hdr, also_auth_header),
            daemon=True
        ).start()

if __name__ == "__main__":
    # Pull defaults from env HTTPS_PROXY if available (to auto-make Basic header)
    hp = os.environ.get("HTTPS_PROXY") or os.environ.get("HTTP_PROXY") or ""
    u = p = ""
    if hp:
        s = urlsplit(hp)
        u = s.username or ""
        p = s.password or ""

    ap = argparse.ArgumentParser()
    ap.add_argument("--listen", default="127.0.0.1:8888")
    ap.add_argument("--target", default=f"{os.environ.get('TARGET_PROXY_HOST','')}:{os.environ.get('TARGET_PROXY_PORT','')}")
    ap.add_argument("--bearer", default=os.environ.get("PROXY_BEARER","") or p)
    ap.add_argument("--also-auth-header", action="store_true", help="Send Authorization: ... alongside Proxy-Authorization")
    ap.add_argument("--with-basic", action="store_true", help="Also try Basic from HTTPS_PROXY creds before giving up")
    args = ap.parse_args()

    if ":" not in args.target:
        print("Set TARGET_PROXY_HOST and TARGET_PROXY_PORT or pass --target host:port", file=sys.stderr)
        sys.exit(2)

    lh, lp = args.listen.split(":")
    th, tp = args.target.split(":")

    basic_hdr = mk_basic(u, args.bearer) if args.with_basic and u else None
    serve(lh, int(lp), th, int(tp), args.bearer, basic_hdr, args.also_auth_header)
