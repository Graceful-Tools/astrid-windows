#!/bin/sh
# Transient helper for an interactive /fixall run: take the working-tree lock with the
# harness session's pid (this script's parent's parent is the harness shell).
cd /c/Users/jonpa/mycode/astrid-dev/astrid-web || exit 1
echo "HARNESS_PID=$PPID"
npx tsx scripts/fixall-session.ts acquire --pid "$PPID" --harness claude-code 2>&1
echo "EXIT=$?"
