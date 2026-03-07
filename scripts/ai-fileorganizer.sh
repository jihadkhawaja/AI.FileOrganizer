#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/AI.FileOrganizer.CLI/AI.FileOrganizer.CLI.csproj"

show_usage() {
    cat <<'EOF'
Usage: ./scripts/ai-fileorganizer.sh <command> [args]

Commands:
  chat                       Start the interactive CLI
  create-job                 Create a scheduled job interactively
  list-jobs                  List configured scheduled jobs
  job-template <name>        Print a YAML job template
  run-job <name>             Run a configured job once
  task-command <name>        Print the Windows Task Scheduler action
  help                       Show this help
EOF
}

invoke_cli() {
    dotnet run --project "${PROJECT_PATH}" -- "$@"
}

COMMAND="${1:-help}"
shift || true

case "${COMMAND}" in
    chat)
        invoke_cli
        ;;
    create-job)
        invoke_cli --create-job
        ;;
    list-jobs)
        invoke_cli --list-jobs
        ;;
    job-template)
        if [[ $# -lt 1 ]]; then
            echo "Missing job name. Usage: ./scripts/ai-fileorganizer.sh job-template <name>" >&2
            exit 1
        fi

        invoke_cli --job-template "$1"
        ;;
    run-job)
        if [[ $# -lt 1 ]]; then
            echo "Missing job name. Usage: ./scripts/ai-fileorganizer.sh run-job <name>" >&2
            exit 1
        fi

        invoke_cli --job "$1"
        ;;
    task-command)
        if [[ $# -lt 1 ]]; then
            echo "Missing job name. Usage: ./scripts/ai-fileorganizer.sh task-command <name>" >&2
            exit 1
        fi

        invoke_cli --task-command "$1"
        ;;
    help)
        show_usage
        ;;
    *)
        echo "Unknown command '${COMMAND}'. Run ./scripts/ai-fileorganizer.sh help" >&2
        exit 1
        ;;
esac