"""Runs one SQL statement on a SQLite file and prints the rows as GMod's
sql.Query would return them: every value a string, NULL as "NULL"."""

import json
import sqlite3
import sys

db, query_file = sys.argv[1], sys.argv[2]
with open(query_file, encoding="utf-8") as f:
    query = f.read()

conn = sqlite3.connect(db)
try:
    cur = conn.execute(query)
    rows = cur.fetchall()
    names = [d[0] for d in cur.description] if cur.description else []
    conn.commit()
    out = [{n: "NULL" if v is None else str(v) for n, v in zip(names, row)} for row in rows]
    print(json.dumps({"ok": True, "rows": out}))
except sqlite3.Error as e:
    print(json.dumps({"ok": False, "error": str(e)}))
