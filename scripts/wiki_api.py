"""Obtain and use Magazedia API tokens without putting them on the command line or in chat.

Tokens are saved under .local/tokens/ (Git-ignored) and are never printed.

  python scripts/wiki_api.py token dev                      # DevvyTheBot, from .local/DevvyTheBot.json
  python scripts/wiki_api.py token prod --user MaggieThebot # asks YOU for the password
  python scripts/wiki_api.py status prod
  python scripts/wiki_api.py request prod GET api/article/gq-hype
  python scripts/wiki_api.py request prod PUT api/article/gq-hype --json body.json
"""

import argparse
import base64
from datetime import datetime, timezone
import getpass
import json
import os
from pathlib import Path
import ssl
import sys
import urllib.error
import urllib.request

RepositoryRoot = Path(__file__).resolve().parent.parent
TokenDirectory = RepositoryRoot / ".local" / "tokens"
DevCredentialsFile = RepositoryRoot / ".local" / "DevvyTheBot.json"
# WIKI_API_DEV_PORT selects another local instance, such as the 7127 preview server.
DevPort = os.environ.get("WIKI_API_DEV_PORT", "7126")
BaseUrls = {"dev": f"https://localhost:{DevPort}", "prod": "https://en.magazedia.com"}
# The local development certificate covers "localhost" but not "en.localhost", so dev requests
# connect to localhost and name the English site in the Host header instead.
HostHeaders = {"dev": f"en.localhost:{DevPort}"}
NameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"


def Send(Environment: str, Method: str, Path_: str, Body: bytes | None, Headers: dict[str, str]) -> tuple[int, str]:
    """Send one HTTPS request to an environment and return the status and response text, including error responses."""
    if Environment in HostHeaders:
        Headers = {**Headers, "Host": HostHeaders[Environment]}
    Request = urllib.request.Request(BaseUrls[Environment] + Path_, data=Body, method=Method, headers=Headers)
    try:
        with urllib.request.urlopen(Request, context=ssl.create_default_context(), timeout=60) as Response:
            return Response.status, Response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as Error:
        return Error.code, Error.read().decode("utf-8", "replace")


def TokenFile(Environment: str) -> Path:
    """Return where the token for an environment is stored."""
    return TokenDirectory / f"{Environment}.jwt"


def ReadClaims(Token: str) -> dict:
    """Decode the JWT payload for display only; the server is what verifies the signature."""
    Payload = Token.split(".")[1]
    return json.loads(base64.urlsafe_b64decode(Payload + "=" * (-len(Payload) % 4)))


def Describe(Token: str) -> str:
    """Summarise who a token is for and when it expires, without revealing the token."""
    Claims = ReadClaims(Token)
    Expires = datetime.fromtimestamp(Claims["exp"], timezone.utc)
    State = "EXPIRED" if Expires <= datetime.now(timezone.utc) else "valid"
    return f"user {Claims.get(NameClaim, '?')}, expires {Expires:%Y-%m-%d %H:%M} UTC ({State})"


def GetToken(Environment: str, UserName: str | None) -> int:
    """Exchange a username and password for a token and save it.

    Dev uses the DevvyTheBot test credentials file. Prod prompts the person at the
    keyboard for the password and refuses to read it from a pipe, so it can only be
    typed by a human in an interactive terminal.
    """
    if Environment == "dev":
        Credentials = json.loads(DevCredentialsFile.read_text(encoding="utf-8"))
        UserName, Password = Credentials["UserName"], Credentials["Password"]
    else:
        if not UserName:
            print("Pass --user for production.", file=sys.stderr)
            return 2
        if not sys.stdin.isatty():
            print("Production passwords must be typed interactively.", file=sys.stderr)
            return 2
        Password = getpass.getpass(f"Password for {UserName} on {BaseUrls[Environment]}: ")

    Status, Text = Send(Environment, "POST", "/api/auth/token",
                        json.dumps({"Username": UserName, "Password": Password}).encode(),
                        {"Content-Type": "application/json"})
    if Status != 200:
        print(f"Token request failed: {Status} {Text}", file=sys.stderr)
        return 1

    Response = json.loads(Text)
    Token = Response.get("Token") or Response["token"]
    TokenDirectory.mkdir(parents=True, exist_ok=True)
    TokenFile(Environment).write_text(Token, encoding="ascii")
    print(f"Saved {TokenFile(Environment).relative_to(RepositoryRoot)}: {Describe(Token)}")
    return 0


def ShowStatus(Environment: str) -> int:
    """Report whether a saved token exists and when it expires."""
    if not TokenFile(Environment).exists():
        print(f"No {Environment} token. Run: python scripts/wiki_api.py token {Environment}")
        return 1
    print(Describe(TokenFile(Environment).read_text(encoding="ascii").strip()))
    return 0


def CallApi(Environment: str, Method: str, Path_: str, JsonFile: Path | None) -> int:
    """Call an API path with the saved bearer token and print the status and response body."""
    if not TokenFile(Environment).exists():
        print(f"No {Environment} token. Run: python scripts/wiki_api.py token {Environment}", file=sys.stderr)
        return 1
    Token = TokenFile(Environment).read_text(encoding="ascii").strip()
    if ReadClaims(Token)["exp"] <= datetime.now(timezone.utc).timestamp():
        print(f"The {Environment} token has expired; get a new one.", file=sys.stderr)
        return 1

    # Accept "api/..." as well as "/api/...": Git Bash rewrites arguments starting with "/" into Windows paths.
    Path_ = "/" + Path_.lstrip("/")
    Headers = {"Authorization": f"Bearer {Token}", "Accept": "application/json"}
    Body = None
    if JsonFile is not None:
        Body = JsonFile.read_bytes()
        Headers["Content-Type"] = "application/json"
    Status, Text = Send(Environment, Method.upper(), Path_, Body, Headers)
    print(Status)
    print(Text)
    return 0 if 200 <= Status < 300 else 1


def Main() -> int:
    """Parse the subcommand and run it."""
    Parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    Commands = Parser.add_subparsers(dest="command", required=True)

    TokenCommand = Commands.add_parser("token", help="get and save a token")
    TokenCommand.add_argument("environment", choices=BaseUrls)
    TokenCommand.add_argument("--user", help="account name (required for prod)")

    StatusCommand = Commands.add_parser("status", help="show the saved token's user and expiry")
    StatusCommand.add_argument("environment", choices=BaseUrls)

    RequestCommand = Commands.add_parser("request", help="call the API with the saved token")
    RequestCommand.add_argument("environment", choices=BaseUrls)
    RequestCommand.add_argument("method")
    RequestCommand.add_argument("path", help="for example api/article/gq-hype")
    RequestCommand.add_argument("--json", type=Path, help="file holding the JSON request body")

    Arguments = Parser.parse_args()
    if Arguments.command == "token":
        return GetToken(Arguments.environment, Arguments.user)
    if Arguments.command == "status":
        return ShowStatus(Arguments.environment)
    return CallApi(Arguments.environment, Arguments.method, Arguments.path, Arguments.json)


if __name__ == "__main__":
    sys.exit(Main())
