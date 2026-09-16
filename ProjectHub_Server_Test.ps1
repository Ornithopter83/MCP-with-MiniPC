$ErrorActionPreference = "Stop"

$ProjectRoot = "C:\AI-Server\ProjectHub"
$ServerProject = Join-Path $ProjectRoot "src\ProjectHub.Server\ProjectHub.Server.csproj"
$PrivateKeyPath = Join-Path $ProjectRoot "src\ProjectHub.Server\projecthub-private.pem"

if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
    throw "ProjectHub private key was not found: $PrivateKeyPath"
}

# Preserve PEM line breaks; never print the key or commit it.
$pem = [IO.File]::ReadAllText($PrivateKeyPath)
$env:PROJECTHUB_ASSERTION_PRIVATE_KEY_PEM = $pem

Set-Location -LiteralPath $ProjectRoot
dotnet run --project $ServerProject
