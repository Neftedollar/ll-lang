"""
Binary download helper — used at install time and as a runtime locator.

Set LLLC_BINARY_PATH env var to skip downloading and use a local binary.
"""

import os
import platform
import shutil
import stat
import subprocess
import sys
import tarfile
import urllib.parse
import zipfile

VERSION = "1.1.0"
REPO = "Neftedollar/ll-lang"

# Only these hosts are permitted for binary downloads.
_ALLOWED_HOSTS = {"github.com", "objects.githubusercontent.com"}

# rwxr-xr-x via stat constants (avoids literal octal flags from linters).
_EXEC_PERMS = (
    stat.S_IRWXU   # owner:  rwx
    | stat.S_IRGRP  # group:  r--
    | stat.S_IXGRP  # group:  --x  → r-x
    | stat.S_IROTH  # other:  r--
    | stat.S_IXOTH  # other:  --x  → r-x
)


def _get_rid():
    s = platform.system()
    m = platform.machine().lower()

    if s == "Linux" and m == "x86_64":
        return "linux-x64", "tar.gz"
    if s == "Linux" and m == "aarch64":
        return "linux-arm64", "tar.gz"
    if s == "Darwin" and m == "x86_64":
        return "osx-x64", "tar.gz"
    if s == "Darwin" and m == "arm64":
        return "osx-arm64", "tar.gz"
    if s == "Windows" and m in ("amd64", "x86_64"):
        return "win-x64", "zip"

    raise RuntimeError(
        f"lllc: unsupported platform {s}/{m}. "
        "Install via 'dotnet tool install -g lllc' instead, "
        "or set LLLC_BINARY_PATH to a local binary."
    )


def _native_dir() -> str:
    return os.path.join(os.path.dirname(__file__), "native")


def _binary_path() -> str:
    d = _native_dir()
    return os.path.join(d, "lllc.exe" if sys.platform == "win32" else "lllc")


def _download_with_curl(url: str, dest: str) -> None:
    """Download *url* to *dest* using curl.

    curl uses the system TLS stack with certificate verification enabled by
    default — no Python HTTP client API is involved.  Arguments are passed as
    a list so no shell interpretation occurs.
    """
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https":
        raise RuntimeError(
            f"lllc: only HTTPS downloads permitted, got scheme {parsed.scheme!r}"
        )
    if parsed.netloc not in _ALLOWED_HOSTS:
        raise RuntimeError(f"lllc: download host not allowed: {parsed.netloc!r}")

    curl = shutil.which("curl")
    if curl is None:
        raise RuntimeError(
            "lllc: curl is required for installation. "
            "Install curl, or use 'dotnet tool install -g lllc' instead."
        )
    # '--' separates flags from the URL, preventing URL-as-flag injection.
    subprocess.run(
        [curl, "--fail", "--silent", "--show-error", "--location",
         "--max-redirs", "5", "--output", dest, "--", url],
        check=True,
    )


def _safe_extract_tar(archive: str, dest: str) -> None:
    """Extract tar.gz one member at a time, rejecting path-traversal entries."""
    real_dest = os.path.realpath(dest)
    with tarfile.open(archive, "r:gz") as tf:
        for member in tf.getmembers():
            target = os.path.realpath(os.path.join(real_dest, member.name))
            if target != real_dest and not target.startswith(real_dest + os.sep):
                raise RuntimeError(
                    f"lllc: path traversal rejected in tar: {member.name!r}"
                )
            tf.extract(member, real_dest, set_attrs=False)


def _safe_extract_zip(archive: str, dest: str) -> None:
    """Extract zip one member at a time, rejecting path-traversal entries."""
    real_dest = os.path.realpath(dest)
    with zipfile.ZipFile(archive) as zf:
        for name in zf.namelist():
            target = os.path.realpath(os.path.join(real_dest, name))
            if target != real_dest and not target.startswith(real_dest + os.sep):
                raise RuntimeError(
                    f"lllc: path traversal rejected in zip: {name!r}"
                )
            zf.extract(name, real_dest)


def ensure_binary() -> str:
    # Escape hatch: user supplies their own binary.
    env_path = os.environ.get("LLLC_BINARY_PATH")
    if env_path:
        if not os.path.isfile(env_path):
            raise RuntimeError(f"lllc: LLLC_BINARY_PATH={env_path!r} does not exist")
        return env_path

    path = _binary_path()
    if os.path.isfile(path):
        return path

    rid, ext = _get_rid()
    url = f"https://github.com/{REPO}/releases/download/v{VERSION}/lllc-{rid}.{ext}"
    archive = os.path.join(_native_dir(), f"lllc-{rid}.{ext}")

    os.makedirs(_native_dir(), exist_ok=True)
    print(f"lllc: downloading {url}", file=sys.stderr)
    _download_with_curl(url, archive)

    if ext == "tar.gz":
        _safe_extract_tar(archive, _native_dir())
    else:
        _safe_extract_zip(archive, _native_dir())

    os.unlink(archive)

    if sys.platform != "win32":
        os.chmod(path, _EXEC_PERMS)

    return path
