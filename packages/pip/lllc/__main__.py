"""Entry point: python -m lllc  or  the lllc console_script."""

import subprocess
import sys

from lllc._download import ensure_binary


def main() -> None:
    binary = ensure_binary()
    result = subprocess.run([binary] + sys.argv[1:])
    sys.exit(result.returncode)


if __name__ == "__main__":
    main()
