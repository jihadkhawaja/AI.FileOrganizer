param(
    [Parameter(Position = 0)]
    [string]$Command = "help",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$projectPath = Join-Path $repoRoot "AI.FileOrganizer.CLI/AI.FileOrganizer.CLI.csproj"

function Show-Usage {
    Write-Host "Usage: ./scripts/ai-fileorganizer.ps1 <command> [args]"
    Write-Host ""
    Write-Host "Commands:"
    Write-Host "  chat                       Start the interactive CLI"
    Write-Host "  create-job                 Create a scheduled job interactively"
    Write-Host "  list-jobs                  List configured scheduled jobs"
    Write-Host "  job-template <name>        Print a YAML job template"
    Write-Host "  run-job <name>             Run a configured job once"
    Write-Host "  task-command <name>        Print the Windows Task Scheduler action"
    Write-Host "  help                       Show this help"
}

function Invoke-Cli([string[]]$CliArguments) {
    & dotnet run --project $projectPath -- @CliArguments
}

switch ($Command.ToLowerInvariant()) {
    "chat" {
        Invoke-Cli @()
    }
    "create-job" {
        Invoke-Cli @("--create-job")
    }
    "list-jobs" {
        Invoke-Cli @("--list-jobs")
    }
    "job-template" {
        if ($Arguments.Count -lt 1) {
            throw "Missing job name. Usage: ./scripts/ai-fileorganizer.ps1 job-template <name>"
        }

        Invoke-Cli @("--job-template", $Arguments[0])
    }
    "run-job" {
        if ($Arguments.Count -lt 1) {
            throw "Missing job name. Usage: ./scripts/ai-fileorganizer.ps1 run-job <name>"
        }

        Invoke-Cli @("--job", $Arguments[0])
    }
    "task-command" {
        if ($Arguments.Count -lt 1) {
            throw "Missing job name. Usage: ./scripts/ai-fileorganizer.ps1 task-command <name>"
        }

        Invoke-Cli @("--task-command", $Arguments[0])
    }
    "help" {
        Show-Usage
    }
    default {
        throw "Unknown command '$Command'. Run ./scripts/ai-fileorganizer.ps1 help"
    }
}