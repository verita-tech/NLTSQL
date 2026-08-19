#!/usr/bin/env python3
"""
One-time Metabase setup for the NLTSQL prototype.

Performs the steps that would otherwise be click-work, and prints the
values to paste into .env:

  1. completes first-run setup (or signs in, if Metabase is already set up)
  2. creates a server-to-server API key
  3. enables static embedding and reads the embedding secret
  4. registers Cube's SQL API as a Postgres database

Standard library only, so it runs anywhere Python 3.9+ does:

    python3 infra/metabase/bootstrap.py

Environment variables (all optional, defaults suit docker-compose):

    METABASE_URL          http://localhost:3000
    MB_ADMIN_EMAIL        admin@example.com
    MB_ADMIN_PASSWORD     Prototype-1234!
    MB_ADMIN_FIRST_NAME   Prototyp
    MB_ADMIN_LAST_NAME    Administrator
    CUBE_SQL_HOST         cube          (as seen from the Metabase container)
    CUBE_SQL_PORT         15432
    CUBEJS_SQL_USER       metabase
    CUBEJS_SQL_PASSWORD   (required)

The script is idempotent enough to re-run: an existing setup is detected
and an existing Cube database entry is reused rather than duplicated.
"""

from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE_URL = os.environ.get("METABASE_URL", "http://localhost:3000").rstrip("/")
ADMIN_EMAIL = os.environ.get("MB_ADMIN_EMAIL", "admin@example.com")
ADMIN_PASSWORD = os.environ.get("MB_ADMIN_PASSWORD", "Prototype-1234!")
ADMIN_FIRST = os.environ.get("MB_ADMIN_FIRST_NAME", "Prototyp")
ADMIN_LAST = os.environ.get("MB_ADMIN_LAST_NAME", "Administrator")

CUBE_HOST = os.environ.get("CUBE_SQL_HOST", "cube")
CUBE_PORT = int(os.environ.get("CUBE_SQL_PORT", "15432"))
CUBE_USER = os.environ.get("CUBEJS_SQL_USER", "metabase")
CUBE_PASSWORD = os.environ.get("CUBEJS_SQL_PASSWORD", "")

DATABASE_NAME = "Cube (semantische Schicht)"


class MetabaseError(RuntimeError):
    pass


def request(method, path, payload=None, session=None, api_key=None):
    """Sends one API request and returns the decoded body (or None)."""
    url = f"{BASE_URL}{path}"
    data = json.dumps(payload).encode() if payload is not None else None

    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")

    if session:
        req.add_header("X-Metabase-Session", session)
    if api_key:
        req.add_header("x-api-key", api_key)

    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            body = response.read()
            return json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode(errors="replace")
        raise MetabaseError(f"{method} {path} -> {error.code}: {detail}") from error
    except urllib.error.URLError as error:
        raise MetabaseError(f"{method} {path} not reachable: {error.reason}") from error


def wait_until_healthy(timeout_seconds=300):
    print(f"Waiting for Metabase at {BASE_URL} ...", flush=True)
    deadline = time.time() + timeout_seconds

    while time.time() < deadline:
        try:
            health = request("GET", "/api/health")
            if health:
                print("Metabase is up.")
                return
        except MetabaseError:
            pass
        time.sleep(3)

    raise MetabaseError("Metabase did not become healthy in time.")


def authenticate():
    """Completes first-run setup, or signs in when already configured."""
    properties = request("GET", "/api/session/properties") or {}
    setup_token = properties.get("setup-token")

    if setup_token:
        print("Running first-run setup ...")
        result = request("POST", "/api/setup", {
            "token": setup_token,
            "user": {
                "first_name": ADMIN_FIRST,
                "last_name": ADMIN_LAST,
                "email": ADMIN_EMAIL,
                "password": ADMIN_PASSWORD,
                "site_name": "NLTSQL",
            },
            "prefs": {"site_name": "NLTSQL", "allow_tracking": False},
        })
        session = (result or {}).get("id")
        if not session:
            raise MetabaseError("Setup returned no session id.")
        return session

    print("Metabase is already set up; signing in ...")
    result = request("POST", "/api/session", {
        "username": ADMIN_EMAIL,
        "password": ADMIN_PASSWORD,
    })
    session = (result or {}).get("id")
    if not session:
        raise MetabaseError("Sign-in returned no session id.")
    return session


def create_api_key(session):
    groups = request("GET", "/api/permissions/group", session=session) or []
    admin_group = next((g for g in groups if g.get("name") == "Administrators"), None)
    if not admin_group:
        raise MetabaseError("Administrators group not found.")

    name = "NLTSQL Prototype"
    existing = request("GET", "/api/api-key", session=session) or []

    if any(k.get("name") == name for k in existing):
        # The unmasked key is only ever returned once, at creation time.
        raise MetabaseError(
            f'An API key named "{name}" already exists. Delete it in '
            "Admin -> Settings -> Authentication -> API keys and re-run, "
            "or set METABASE_API_KEY in .env by hand."
        )

    created = request("POST", "/api/api-key", {
        "name": name,
        "group_id": admin_group["id"],
    }, session=session)

    key = (created or {}).get("unmasked_key")
    if not key:
        raise MetabaseError("Metabase returned no API key.")
    return key


def enable_static_embedding(session):
    request("PUT", "/api/setting/enable-embedding-static", {"value": True}, session=session)

    secret = request("GET", "/api/setting/embedding-secret-key", session=session)

    # Settings come back either bare or wrapped depending on version.
    if isinstance(secret, dict):
        secret = secret.get("value")
    if not secret:
        raise MetabaseError("Could not read the embedding secret key.")
    return secret


def register_cube_database(session):
    if not CUBE_PASSWORD:
        raise MetabaseError("CUBEJS_SQL_PASSWORD is not set; export it or source your .env first.")

    databases = request("GET", "/api/database", session=session) or {}
    entries = databases.get("data", databases if isinstance(databases, list) else [])

    for entry in entries:
        if entry.get("name") == DATABASE_NAME:
            print(f"Database entry already exists (id {entry['id']}).")
            return entry["id"]

    created = request("POST", "/api/database", {
        "name": DATABASE_NAME,
        "engine": "postgres",
        "details": {
            "host": CUBE_HOST,
            "port": CUBE_PORT,
            # Cube's SQL API exposes the model as a database of this name.
            "dbname": "cube",
            "user": CUBE_USER,
            "password": CUBE_PASSWORD,
            "ssl": False,
        },
    }, session=session)

    database_id = (created or {}).get("id")
    if not database_id:
        raise MetabaseError("Metabase returned no database id.")
    return database_id


def main():
    wait_until_healthy()

    session = authenticate()
    api_key = create_api_key(session)
    embedding_secret = enable_static_embedding(session)
    database_id = register_cube_database(session)

    print("\nDone. Put these into .env:\n")
    print(f"METABASE_API_KEY={api_key}")
    print(f"METABASE_EMBEDDING_SECRET_KEY={embedding_secret}")
    print(f"METABASE_CUBE_DATABASE_ID={database_id}")
    print(
        "\nMB_EMBEDDING_SECRET_KEY in docker-compose must match "
        "METABASE_EMBEDDING_SECRET_KEY, otherwise embedded charts stay blank.\n"
        "Then restart Metabase so it picks up the value."
    )


if __name__ == "__main__":
    try:
        main()
    except MetabaseError as error:
        print(f"\nFehler: {error}", file=sys.stderr)
        sys.exit(1)
