import io

import pandas as pd
import psycopg

_CHUNK_ROWS = 200_000


def copy_frame(cursor: psycopg.Cursor, table: str, frame: pd.DataFrame, columns: list[str]) -> None:
    """Bulk-loads the frame with COPY (CSV: empty unquoted fields are NULL)."""
    statement = f"COPY {table} ({', '.join(columns)}) FROM STDIN WITH (FORMAT csv)"
    with cursor.copy(statement) as copy:
        for start in range(0, len(frame), _CHUNK_ROWS):
            buffer = io.StringIO()
            frame.iloc[start : start + _CHUNK_ROWS][columns].to_csv(buffer, index=False, header=False)
            copy.write(buffer.getvalue())


def copy_query(cursor: psycopg.Cursor, query: str) -> pd.DataFrame:
    """Reads a query result with COPY TO (much faster than fetching rows for millions of rows)."""
    buffer = io.BytesIO()
    with cursor.copy(f"COPY ({query}) TO STDOUT WITH (FORMAT csv, HEADER)") as copy:
        for data in copy:
            buffer.write(data)
    buffer.seek(0)
    return pd.read_csv(buffer)
