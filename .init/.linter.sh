#!/bin/bash
cd /home/kavia/workspace/code-generation/outage-management-and-dispatch-platform-3142-3151/backend_api
dotnet build --no-restore -v quiet -nologo -consoleloggerparameters:NoSummary /p:TreatWarningsAsErrors=false
LINT_EXIT_CODE=$?
if [ $LINT_EXIT_CODE -ne 0 ]; then
  exit 1
fi

