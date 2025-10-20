#!/usr/bin/env python3
import argparse, os, socket, threading, select, sys

# Simple TCP relay
def pump(a, b):
    try:
        sockets = [a, b]
        while True:
            r, _, _ = select.select(sockets, [], [], 60)
            if a in r:
                data = a.recv(65536)
                if not data: break
                b.sendall(data)
            if b in r:
                data = b.recv(65536)
                if not data: break
                a.sendall(data)
    finally:
        try: a.shutdown(socket.SHUT_RDWR)
        except: pass
        try: b.shutdown(socket.SHUT_RDWR)
        except: pass
        a.close(); b.close()

def recv_until(sock, sep=b"\r\n\r\n", limit=65536):
    buf = b""
    while sep not in buf:
        chunk = sock.recv(4096)
        if not chunk: break
        buf += chunk
        if len(buf) > limit: break
    return buf

def handle_client(client, target_host, target_port, bearer):
    try:
        # Read the first HTTP request (either CONNECT or regular)
        first = recv_until(client)
        if not first:
            client.close(); return

        # Parse the first line and headers
        try:
            head, rest = first.split(b"\r\n", 1)
        except ValueError:
            client.close(); return
        method, path, proto = head.decode("latin-1").split(" ", 2)

        # Open socket to CORP proxy
        corp = socket.create_connection((target_host, target_port), timeout=30)

        if method.upper() == "CONNECT":
            # CONNECT host:port using Bearer header at corp proxy
            req = (
                f"CONNECT {path} HTTP/1.1\r\n"
                f"Host: {path}\r\n"
                f"Proxy-Authorization: Bearer {bearer}\r\n"
                f"Connection: keep-alive\r\n\r\n"
            ).encode("latin-1")
            corp.sendall(req)

            # Read corp proxy response header
            resp = recv_until(corp)
            if b" 200 " not in resp.split(b"\r\n",1)[0]:
                client.sendall(resp or b"HTTP/1.1 502 Bad Gateway\r\n\r\n")
                corp.close(); client.close(); return

            # RFC: upon 200, tell client OK and then tunnel bytes
            client.sendall(b"HTTP/1.1 200 Connection Established\r\n\r\n")
            pump(client, corp)
            return
        else:
            # Plain HTTP: forward full request through corp proxy
            # Reconstruct headers, inject Bearer, ensure absolute-URI in request line
            lines = first.split(b"\r\n")
            # Some clients send relative paths; build absolute form for proxy if needed
            # We'll trust client provided absolute URL in 'path', otherwise we pass as-is.
            new = [f"{method} {path} {proto}".encode("latin-1")]
            saw_proxy_auth = False
            for ln in lines[1:]:
                if ln.lower().startswith(b"proxy-authorization:"):
                    saw_proxy_auth = True
                    continue  # replace it
                if ln == b"": break
                new.append(ln)
            new.append(f"Proxy-Authorization: Bearer {bearer}".encode("latin-1"))
            new.append(b"Connection: keep-alive")
            new.append(b"")
            new.append(b"")
            corp.sendall(b"\r\n".join(new))
            # If there was a request body already buffered, it's in 'rest'; send it
            # (We already included a blank line, so just send the remainder bytes after headers)
            body_start = first.split(b"\r\n\r\n",1)
            if len(body_start)==2 and body_start[1]:
                corp.sendall(body_start[1])
            pump(client, corp)
            return
    except Exception as e:
        try:
            client.sendall(b"HTTP/1.1 500 Internal Server Error\r\n\r\n")
        except: pass
        try: client.close()
        except: pass

def serve(listen_host, listen_port, target_host, target_port, bearer):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((listen_host, listen_port))
    srv.listen(128)
    print(f"[bearer-proxy] listening on {listen_host}:{listen_port} -> proxy {target_host}:{target_port} (Bearer <token>)", flush=True)
    while True:
        c, _ = srv.accept()
        threading.Thread(target=handle_client, args=(c, target_host, target_port, bearer), daemon=True).start()

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--listen", default="127.0.0.1:8888")
    ap.add_argument("--target", default=os.environ.get("TARGET_PROXY",""))
    ap.add_argument("--bearer", default=os.environ.get("PROXY_BEARER",""))
    args = ap.parse_args()

    lh, lp = args.listen.split(":")
    if args.target:
        th, tp = args.target.split(":")
    else:
        # Or read from env TARGET_PROXY_HOST / TARGET_PROXY_PORT
        th = os.environ.get("TARGET_PROXY_HOST","")
        tp = os.environ.get("TARGET_PROXY_PORT","")
    if not th or not tp or not args.bearer:
        print("Set TARGET_PROXY_HOST, TARGET_PROXY_PORT, and PROXY_BEARER (JWT).", file=sys.stderr)
        sys.exit(2)
    serve(lh, int(lp), th, int(tp), args.bearer)
