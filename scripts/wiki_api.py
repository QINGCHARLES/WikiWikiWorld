"""Obtain and use WikiWikiWorld API tokens without putting them on the command line or in chat.

Tokens are saved under .local/tokens/ (Git-ignored) and are never printed.

  python scripts/wiki_api.py token dev                      # DevvyTheBot, from .local/DevvyTheBot.json
  python scripts/wiki_api.py token prod --user MaggieTheBot # asks YOU for the password; lasts 180 days (--days N)
  python scripts/wiki_api.py status prod
  python scripts/wiki_api.py request prod GET api/article/gq-hype
  python scripts/wiki_api.py request prod --host en.cinepedia.wiki GET api/article/the-matrix
  python scripts/wiki_api.py request dev --host en.cinepedia.wiki.localhost GET api/article/the-matrix
  python scripts/wiki_api.py request prod POST api/article/new-slug --form Title="New Title" --form Type=Article --form-file Text=article.md --form RevisionReason="Create article" --prompts common=6,magazine=26
  python scripts/wiki_api.py request prod PUT api/article/gq-hype --json body.json --prompts common=6,magazine=26

Creating or updating a Magazedia article needs --prompts with the prompt versions the text follows. The save is refused
unless they match the newest versions on main (checked after a fetch); --prompts skip is for saves that are not article
writing, such as category pages or mechanical repairs.
  python scripts/wiki_api.py request prod PUT api/article/old-slug --json body.json   # body with "NewUrlSlug": "new-slug" renames
  python scripts/wiki_api.py request prod POST api/file/some-file-slug --file File=pic.webp --form Culture=en
"""

import argparse
import base64
from datetime import datetime, timezone
import getpass
import json
import mimetypes
import os
from pathlib import Path
import re
import ssl
import subprocess
import sys
import urllib.error
import urllib.request
import uuid

RepositoryRoot = Path(__file__).resolve().parent.parent
TokenDirectory = RepositoryRoot / ".local" / "tokens"
DevCredentialsFile = RepositoryRoot / ".local" / "DevvyTheBot.json"
# WIKI_API_DEV_PORT selects another local instance, such as the 7127 preview server.
DevPort = os.environ.get("WIKI_API_DEV_PORT", "7126")
DefaultHosts = {
    "dev": "en.magazedia.com.localhost",
    "prod": "en.magazedia.com",
}
NameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
# Windows' registry-backed MIME table often lacks these image types; the upload API checks the declared type.
mimetypes.add_type("image/webp", ".webp")
mimetypes.add_type("image/avif", ".avif")


def ResolveTarget(Environment: str, HostOverride: str | None) -> tuple[str, str | None]:
    """Return (BaseUrl, HostHeader) for the given environment and optional --host / WIKI_API_HOST override."""
    Host = (HostOverride or os.environ.get("WIKI_API_HOST") or DefaultHosts[Environment]).strip()
    if Environment == "dev":
        HostWithPort = Host if ":" in Host else f"{Host}:{DevPort}"
        # Connect via localhost so TLS validation succeeds with either the standard localhost cert
        # or the multi-SAN dev cert, and route to the target site/culture via the Host header.
        return f"https://localhost:{DevPort}", HostWithPort
    return f"https://{Host}", None


def Send(
    Environment: str,
    Method: str,
    Path_: str,
    Body: bytes | None,
    Headers: dict[str, str],
    HostOverride: str | None = None,
) -> tuple[int, str]:
    """Send one HTTPS request to an environment and return the status and response text, including error responses."""
    BaseUrl, HostHeader = ResolveTarget(Environment, HostOverride)
    if HostHeader is not None:
        Headers = {**Headers, "Host": HostHeader}
    Request = urllib.request.Request(BaseUrl + Path_, data=Body, method=Method, headers=Headers)
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


def GetToken(Environment: str, UserName: str | None, Days: int | None, HostOverride: str | None) -> int:
    """Exchange a username and password for a token and save it.

    Dev uses the DevvyTheBot test credentials file. Prod prompts the person at the
    keyboard for the password and refuses to read it from a pipe, so it can only be
    typed by a human in an interactive terminal. Days asks for a longer lifetime
    (the server allows up to 365); without it the server issues a 24-hour token.
    """
    BaseUrl, HostHeader = ResolveTarget(Environment, HostOverride)
    TargetLabel = HostHeader or BaseUrl
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
        Password = getpass.getpass(f"Password for {UserName} on {TargetLabel}: ")

    Login = {"Username": UserName, "Password": Password}
    if Days is not None:
        Login["ExpiresInDays"] = Days
    Status, Text = Send(
        Environment,
        "POST",
        "/api/auth/token",
        json.dumps(Login).encode(),
        {"Content-Type": "application/json"},
        HostOverride=HostOverride,
    )
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


def BuildMultipart(Fields: list[str], Files: list[str]) -> tuple[bytes, str]:
    """Encode KEY=VALUE form fields and FIELD=PATH file parts as multipart/form-data.

    Returns the body and its Content-Type header (which carries the boundary).
    """
    Boundary = f"----wiki-api-{uuid.uuid4().hex}"
    Parts: list[bytes] = []
    for Field in Fields:
        Name, Value = Field.split("=", 1)
        Parts.append(f'--{Boundary}\r\nContent-Disposition: form-data; name="{Name}"\r\n\r\n{Value}\r\n'.encode())
    for FilePart in Files:
        Name, FilePath = FilePart.split("=", 1)
        Upload = Path(FilePath)
        ContentType = mimetypes.guess_type(Upload.name)[0] or "application/octet-stream"
        Parts.append(f'--{Boundary}\r\nContent-Disposition: form-data; name="{Name}"; filename="{Upload.name}"\r\n'
                     f"Content-Type: {ContentType}\r\n\r\n".encode() + Upload.read_bytes() + b"\r\n")
    Parts.append(f"--{Boundary}--\r\n".encode())
    return b"".join(Parts), f"multipart/form-data; boundary={Boundary}"


def CallApi(
    Environment: str,
    Method: str,
    Path_: str,
    JsonFile: Path | None,
    Fields: list[str],
    Files: list[str],
    HostOverride: str | None,
) -> int:
    """Call an API path with the saved bearer token and print the status and response body.

    The body is either a JSON file or multipart form fields and files, not both.
    """
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
    elif Fields or Files:
        Body, Headers["Content-Type"] = BuildMultipart(Fields, Files)
    Status, Text = Send(Environment, Method.upper(), Path_, Body, Headers, HostOverride=HostOverride)
    print(Status)
    print(Text)
    return 0 if 200 <= Status < 300 else 1


PromptFiles = {
    "common": "magazedia-common.md",
    "magazine": "magazedia-magazine.md",
    "issue": "magazedia-issue.md",
    "brand": "magazedia-brand.md",
}


def ReadPromptVersion(Ref: str, FileName: str) -> int | None:
    """Return the version number in the header of prompts/FileName at a Git ref, or None if it cannot be read."""
    Result = subprocess.run(
        ["git", "-C", str(RepositoryRoot), "show", f"{Ref}:prompts/{FileName}"],
        capture_output=True, text=True, encoding="utf-8", timeout=30)
    Match = re.search(r"^Version (\d+),", Result.stdout, re.MULTILINE) if Result.returncode == 0 else None
    return int(Match.group(1)) if Match else None


def CurrentPromptVersions() -> dict[str, int]:
    """Return each prompt's newest version on main or origin/main, after fetching origin.

    Branch refs are shared by every worktree, so this sees the latest prompts even from an old worktree or checkout.
    """
    subprocess.run(["git", "-C", str(RepositoryRoot), "fetch", "-q", "origin", "main"], capture_output=True, timeout=60)
    Versions = {}
    for Name, FileName in PromptFiles.items():
        Found = [V for V in (ReadPromptVersion(Ref, FileName) for Ref in ("origin/main", "main")) if V is not None]
        if Found:
            Versions[Name] = max(Found)
    return Versions


def CheckPromptVersions(Declared: str | None) -> str | None:
    """Check the --prompts declaration for an article save; return an error message, or None when it may proceed.

    The writer declares the prompt versions it followed, such as "common=9,magazine=28", adding "brand=1" for a brand article. Saving is refused when any
    is older than the newest on main, so a draft written under superseded rules is rechecked before it is published.
    "skip" is for saves that are not article writing, such as category pages or mechanical repairs.
    """
    Reread = ("Read the current prompts (prompts/README.md; from a worktree, `git show origin/main:prompts/<file>`), "
              "recheck the draft against them, then pass the versions you followed.")
    if Declared is not None and Declared.strip().lower() == "skip":
        return None
    Current = CurrentPromptVersions()
    Common, Magazine = Current.get('common', 'N'), Current.get('magazine', 'N')
    Example = (f"common={Common},magazine={Magazine} (or common={Common},issue={Current.get('issue', 'N')}; "
               f"for a brand article common={Common},magazine={Magazine},brand={Current.get('brand', 'N')})")
    if Declared is None:
        return ("Saving a Magazedia article needs --prompts with the prompt versions the article follows, currently "
                f"--prompts {Example}; --prompts skip only for saves that are not article writing. " + Reread)
    Given = {}
    for Part in Declared.split(","):
        Name, _, Number = Part.strip().partition("=")
        if Name not in PromptFiles or not Number.isdigit():
            return f"Could not read --prompts {Declared!r}; write it as {Example}."
        Given[Name] = int(Number)
    if "common" not in Given or ("magazine" in Given) == ("issue" in Given) or ("brand" in Given and "magazine" not in Given):
        return f"--prompts needs common plus exactly one of magazine or issue, and brand only with magazine: {Example}."
    Stale = [f"{Name} {Given[Name]} (current {Current[Name]})" for Name in Given
             if Name in Current and Given[Name] != Current[Name]]
    if Stale:
        return "Prompt versions do not match main: " + ", ".join(Stale) + ". " + Reread
    return None


def IsArticleSave(Method: str, Path_: str, HostOverride: str | None) -> bool:
    """True for a create or update of a Magazedia article, the saves that must follow the current prompts."""
    Host = HostOverride or os.environ.get("WIKI_API_HOST") or "magazedia"
    return (Method.upper() in {"POST", "PUT"} and re.match(r"/?api/article/", Path_) is not None
            and "magazedia" in Host.lower())


def Main() -> int:
    """Parse the subcommand and run it."""
    # Article text is UTF-8; without this, Windows writes redirected output in the ANSI code page.
    sys.stdout.reconfigure(encoding="utf-8")
    Parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    Parser.add_argument(
        "--host",
        help="target site/culture hostname (for example en.cinepedia.wiki or en.cinepedia.wiki.localhost; "
             "can also be set via WIKI_API_HOST)",
    )
    Commands = Parser.add_subparsers(dest="command", required=True)

    TokenCommand = Commands.add_parser("token", help="get and save a token")
    TokenCommand.add_argument("environment", choices=DefaultHosts)
    TokenCommand.add_argument("--user", help="account name (required for prod)")
    TokenCommand.add_argument("--days", type=int, help="token lifetime in days, 1-365 (default: 180 for prod, 1 for dev)")
    TokenCommand.add_argument("--host", dest="subcommand_host", help="override target hostname")

    StatusCommand = Commands.add_parser("status", help="show the saved token's user and expiry")
    StatusCommand.add_argument("environment", choices=DefaultHosts)

    RequestCommand = Commands.add_parser("request", help="call the API with the saved token")
    RequestCommand.add_argument("environment", choices=DefaultHosts)
    RequestCommand.add_argument("method")
    RequestCommand.add_argument("path", help="for example api/article/gq-hype")
    RequestCommand.add_argument("--host", dest="subcommand_host", help="override target hostname")
    Body = RequestCommand.add_mutually_exclusive_group()
    Body.add_argument("--json", type=Path, help="file holding the JSON request body")
    Body.add_argument("--form", action="append", default=[], metavar="KEY=VALUE", help="multipart form field (repeatable)")
    RequestCommand.add_argument("--file", action="append", default=[], metavar="FIELD=PATH", help="multipart file part (repeatable)")
    RequestCommand.add_argument(
        "--form-file", action="append", default=[], metavar="KEY=PATH",
        help="multipart form field whose value is the UTF-8 text of PATH, such as a whole article (repeatable)")
    RequestCommand.add_argument(
        "--prompts", metavar="common=N,magazine=N",
        help="prompt versions the article follows; required to create or update a Magazedia article, "
             "and refused unless they match the newest on main ('skip' for saves that are not article writing)")

    Arguments = Parser.parse_args()
    HostOverride = getattr(Arguments, "subcommand_host", None) or Arguments.host
    if Arguments.command == "token":
        Days = Arguments.days if Arguments.days is not None else (180 if Arguments.environment == "prod" else None)
        return GetToken(Arguments.environment, Arguments.user, Days, HostOverride)
    if Arguments.command == "status":
        return ShowStatus(Arguments.environment)
    if Arguments.json and (Arguments.file or Arguments.form_file):
        Parser.error("--json cannot be combined with --file or --form-file")
    if IsArticleSave(Arguments.method, Arguments.path, HostOverride):
        Problem = CheckPromptVersions(Arguments.prompts)
        if Problem:
            print(Problem, file=sys.stderr)
            return 2
    # Article text is far longer than a Windows command line allows, so it is read from a file.
    Fields = Arguments.form + [
        f"{Name}={Path(FilePath).read_text(encoding='utf-8')}"
        for Name, FilePath in (FormFile.split("=", 1) for FormFile in Arguments.form_file)
    ]
    return CallApi(
        Arguments.environment,
        Arguments.method,
        Arguments.path,
        Arguments.json,
        Fields,
        Arguments.file,
        HostOverride,
    )


if __name__ == "__main__":
    sys.exit(Main())
