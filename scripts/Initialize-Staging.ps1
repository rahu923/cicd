param(
    [Parameter(Mandatory = $true)]
    [string] $Server,
    [Parameter(Mandatory = $true)]
    [string] $Database,
    [Parameter(Mandatory = $true)]
    [string] $SqlUser
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\cicd.csproj'
Write-Host "Initializing staging database '$Database' on '$Server'."
$settingsPath = Join-Path $PSScriptRoot '..\appsettings.json'
$seedUsers = @((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).IdentitySeed.Users)
if ($seedUsers.Count -eq 0) { throw 'Configure IdentitySeed.Users in appsettings.json first.' }
if (@($seedUsers | Select-Object -ExpandProperty Email -Unique).Count -ne $seedUsers.Count) {
    throw 'Seed users must have different email addresses.'
}
foreach ($seedUser in $seedUsers) {
    if ([string]::IsNullOrWhiteSpace($seedUser.Email) -or $seedUser.Role -notin @('Admin', 'Manager', 'Customer')) {
        throw 'Each seed user must have an email and an Admin, Manager, or Customer role.'
    }
}

function Read-SecretText([string] $Prompt) {
    $secret = Read-Host $Prompt -AsSecureString
    return (New-Object System.Net.NetworkCredential('', $secret)).Password
}

# Build the connection string with proper escaping for password punctuation.
$connection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$connection['Data Source'] = "tcp:$Server,1433"
$connection['Initial Catalog'] = $Database
$connection['User ID'] = $SqlUser
$connection['Password'] = Read-SecretText 'Azure SQL password'
$connection['Encrypt'] = $true
$connection['TrustServerCertificate'] = $false
$connection['Connect Timeout'] = 30

$temporarySettings = @{
    'ASPNETCORE_ENVIRONMENT' = 'Production'
    'DOTNET_ENVIRONMENT' = 'Production'
    'ConnectionStrings__DefaultConnection' = $connection.ConnectionString
    'IdentitySeed__Enabled' = 'true'
}
for ($index = 0; $index -lt $seedUsers.Count; $index++) {
    $seedUser = $seedUsers[$index]
    $temporarySettings["IdentitySeed__Users__${index}__Email"] = $seedUser.Email
    $temporarySettings["IdentitySeed__Users__${index}__Role"] = $seedUser.Role
    $temporarySettings["IdentitySeed__Users__${index}__Password"] = Read-SecretText "Password for $($seedUser.Role) ($($seedUser.Email))"
}
$previousSettings = @{}
try {
    foreach ($key in $temporarySettings.Keys) {
        $previousSettings[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $temporarySettings[$key], 'Process')
    }
    & dotnet run --project $projectPath --configuration Release --no-launch-profile -- --initialize-database
    if ($LASTEXITCODE -ne 0) {
        throw 'Database initialization failed. Check the error above, SQL firewall access, and password requirements.'
    }
    Write-Host 'Staging migrations and default data initialization completed.'
}
finally {
    foreach ($key in $previousSettings.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previousSettings[$key], 'Process')
    }
    $connection.Clear()
    $temporarySettings.Clear()
    $previousSettings.Clear()
}
