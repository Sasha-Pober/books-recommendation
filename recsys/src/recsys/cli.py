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

    training = commands.add_parser(
        "train", help="train the hybrid model on an exported dataset (e.g. on Kaggle)"
    )
    training.add_argument("--data", type=Path, required=True, help="folder written by export-dataset")
    training.add_argument("--out", type=Path, default=Path("artifacts/bundles"))
    training.add_argument("--name", help="bundle name (default: hybrid-<UTC time>)")
    training.add_argument("--device", default="auto", help="auto, cpu, cuda")
    training.add_argument("--max-epochs", type=int, default=30)
    training.add_argument("--eval-users", type=int, default=3000, help="validation users sampled per epoch")
    training.add_argument(
        "--test-users", type=int, default=10000, help="test users sampled at the end (0 = all)"
    )
    training.add_argument("--no-baselines", action="store_true", help="skip popularity, TF-IDF and BPR-MF")
    training.add_argument(
        "--cache-dir", type=Path, default=Path("artifacts/cache"), help="text embedding cache"
    )

    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")

    if args.command == "train":  # needs no database
        from recsys.model.train import TrainConfig, train

        config = TrainConfig(
            max_epochs=args.max_epochs,
            eval_users=args.eval_users,
            test_users=args.test_users,
            baselines=not args.no_baselines,
        )
        train(args.data, args.out, config, device=args.device, name=args.name, cache_dir=args.cache_dir)
        return

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
