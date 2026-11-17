"""Command line: `uv run recsys <command>`."""

import argparse
import logging
from pathlib import Path


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(prog="recsys", description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    commands.add_parser("serve", help="run the HTTP service")

    goodreads = commands.add_parser(
        "import-goodreads", help="import a UCSD Goodreads genre subset into the store database"
    )
    goodreads.add_argument("--data-dir", type=Path, default=Path("data/goodreads"))
    goodreads.add_argument("--genre", default="poetry", help="genre subset name, e.g. poetry, children")
    goodreads.add_argument(
        "--min-user-books",
        type=int,
        default=5,
        help="skip readers with fewer shelved books (their few events add noise and size)",
    )

    export = commands.add_parser("export-dataset", help="export training data from the store database")
    export.add_argument("--out", type=Path, default=Path("data/export"))

    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")

    # Imported here so `serve` doesn't load pandas and the data commands don't load the web stack.
    from recsys.config import Settings

    settings = Settings()
    if args.command == "serve":
        import uvicorn

        uvicorn.run("recsys.main:create_app", factory=True, host=settings.host, port=settings.port)
    elif args.command == "import-goodreads":
        from recsys.goodreads.importer import import_goodreads

        import_goodreads(settings.database_url, args.data_dir, args.genre, args.min_user_books)
    elif args.command == "export-dataset":
        from recsys.dataset.export import export_dataset

        export_dataset(settings.database_url, args.out)
