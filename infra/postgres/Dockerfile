# PostgreSQL with pgvector and files for Ukrainian full-text search.
# The text search configuration itself ("ukrainian") is created by the API's initial EF migration.
FROM pgvector/pgvector:pg17

COPY ukrainian.stop /tmp/ukrainian.stop

# Hunspell uk_UA dictionary from Debian (LibreOffice dictionaries), renamed to the file
# extensions PostgreSQL's ispell template expects.
RUN apt-get update \
    && apt-get install -y --no-install-recommends hunspell-uk \
    && rm -rf /var/lib/apt/lists/* \
    && ts="/usr/share/postgresql/${PG_MAJOR}/tsearch_data" \
    && cp /usr/share/hunspell/uk_UA.aff "$ts/uk_ua.affix" \
    && cp /usr/share/hunspell/uk_UA.dic "$ts/uk_ua.dict" \
    && mv /tmp/ukrainian.stop "$ts/ukrainian.stop"
