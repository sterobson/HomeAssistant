# Deploy Secrets File
# Copy this file to deploy.secrets.ps1 and fill in your Azure Function keys
# deploy.secrets.ps1 is gitignored and should NEVER be committed

# Local development localhost key
# Get this from Frontend/.env.local -> VITE_LOCALHOST_KEY
# This is used when building the frontend for local deployment
$env:LOCALHOST_FUNCTION_KEY = "your-localhost-key-here"

# Testing environment Azure Function key
# Get this from Azure Portal -> Function App (testing) -> App keys
$env:TESTING_FUNCTION_KEY = "your-testing-function-key-here"

# Production environment Azure Function key
# Get this from Azure Portal -> Function App (production) -> App keys
$env:PRODUCTION_FUNCTION_KEY = "your-production-function-key-here"
