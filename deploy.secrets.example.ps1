# Deploy Secrets File
# Copy this file to deploy.secrets.ps1 and fill in your Azure Function keys
# deploy.secrets.ps1 is gitignored and should NEVER be committed

# Testing environment Azure Function key
# Get this from Azure Portal -> Function App (testing) -> App keys
$env:TESTING_FUNCTION_KEY = "your-testing-function-key-here"

# Production environment Azure Function key
# Get this from Azure Portal -> Function App (production) -> App keys
$env:PRODUCTION_FUNCTION_KEY = "your-production-function-key-here"
