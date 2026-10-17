<#
.SYNOPSIS
  Adds 20 sample customers (fake people, real Coimbatore streets) to a running Fitzobit app through its normal API,
  exactly as if they were typed into Add Customer: IDs, end dates, audit entries and location lookups are created by the app.

.EXAMPLE
  .\samples\Add-SampleCustomers.ps1                                   # app started from Visual Studio (IIS Express)
  .\samples\Add-SampleCustomers.ps1 -BaseUrl http://localhost:5080    # app started with .\start.ps1 or the "http" profile

  Asks for your Fitzobit user name and password (nothing is stored). Customers whose phone number already exists are
  skipped, so running it twice adds nothing new. All names and phone numbers are made up.
#>
param(
    [string]$BaseUrl = 'https://localhost:44363',
    [pscredential]$Credential
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$BaseUrl = $BaseUrl.TrimEnd('/')
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# Name, phone, address, pincode, preference, meal type, frequency, start date (Mon-Sat), status (codes from Settings -> Dropdown values)
$customers = @(
    @('Arjun Krishnan',   '9000001001', '12 DB Road, RS Puram, Coimbatore',                '641002', 'VEG',      'MEDIUM',    'MONTHLY', '2026-10-12', 'ACTIVE'),
    @('Kavya Sundaram',   '9000001002', '45 Cross Cut Road, Gandhipuram, Coimbatore',      '641012', 'WITH-EGG', 'REGULAR',   'MONTHLY', '2026-10-10', 'ACTIVE'),
    @('Rahul Venkatesh',  '9000001003', '8 Avinashi Road, Peelamedu, Coimbatore',          '641004', 'NONE',     'LARGE',     'MONTHLY', '2026-10-12', 'ACTIVE'),
    @('Divya Ramesh',     '9000001004', '21 NSR Road, Saibaba Colony, Coimbatore',         '641011', 'VEG',      'MEAL-BOWL', 'MONTHLY', '2026-10-13', 'ACTIVE'),
    @('Sanjay Murugan',   '9000001005', '3 Race Course Road, Race Course, Coimbatore',     '641018', 'WITH-EGG', 'MEAL-BOWL', 'WEEKLY',  '2026-10-12', 'ACTIVE'),
    @('Meera Balaji',     '9000001006', '17 Trichy Road, Ramanathapuram, Coimbatore',      '641045', 'VEG',      'OATS',      'WEEKLY',  '2026-10-14', 'ACTIVE'),
    @('Vignesh Kumar',    '9000001007', '56 Kamaraj Road, Singanallur, Coimbatore',        '641005', 'NONE',     'OATS',      'MONTHLY', '2026-10-15', 'ACTIVE'),
    @('Priya Natarajan',  '9000001008', '9 Sathy Road, Saravanampatti, Coimbatore',        '641035', 'WITH-EGG', 'LARGE',     'WEEKLY',  '2026-10-09', 'ACTIVE'),
    @('Karthik Selvam',   '9000001009', '34 Marudhamalai Road, Vadavalli, Coimbatore',     '641041', 'VEG',      'MEDIUM',    'WEEKLY',  '2026-10-10', 'ACTIVE'),
    @('Lakshmi Prakash',  '9000001010', '14 Mettupalayam Road, Ganapathy, Coimbatore',     '641006', 'VEG',      'REGULAR',   'WEEKLY',  '2026-10-12', 'ACTIVE'),
    @('Harish Mohan',     '9000001011', '2 Big Bazaar Street, Town Hall, Coimbatore',      '641001', 'VEG',      'MEDIUM',    'MONTHLY', '2026-10-05', 'ACTIVE'),
    @('Nithya Raghavan',  '9000001012', '27 Palakkad Road, Kuniyamuthur, Coimbatore',      '641008', 'WITH-EGG', 'MEDIUM',    'MONTHLY', '2026-10-01', 'ACTIVE'),
    @('Suresh Babu',      '9000001013', '61 Mettupalayam Road, Thudiyalur, Coimbatore',    '641034', 'NONE',     'REGULAR',   'MONTHLY', '2026-10-12', 'ACTIVE'),
    @('Anitha Senthil',   '9000001014', '5 Railway Station Road, Podanur, Coimbatore',     '641023', 'VEG',      'MEAL-BOWL', 'WEEKLY',  '2026-10-13', 'ACTIVE'),
    @('Gokul Raj',        '9000001015', '18 Pollachi Main Road, Sundarapuram, Coimbatore', '641024', 'WITH-EGG', 'LARGE',     'MONTHLY', '2026-10-12', 'ACTIVE'),
    @('Swetha Ganesh',    '9000001016', '40 Siruvani Main Road, Kovaipudur, Coimbatore',   '641042', 'VEG',      'MEAL-BOWL', 'MONTHLY', '2026-10-10', 'ACTIVE'),
    @('Pradeep Arumugam', '9000001017', '11 Trichy Road, Ondipudur, Coimbatore',           '641016', 'NONE',     'REGULAR',   'MONTHLY', '2026-10-07', 'PAUSED'),
    @('Deepa Chandran',   '9000001018', '7 Avinashi Road, Kalapatti, Coimbatore',          '641048', 'VEG',      'OATS',      'MONTHLY', '2026-10-12', 'ACTIVE'),
    @('Manoj Subramani',  '9000001019', '23 Trichy Road, Sulur, Coimbatore',               '641402', 'WITH-EGG', 'MEDIUM',    'WEEKLY',  '2026-10-12', 'INACTIVE'),
    @('Revathi Kannan',   '9000001020', '30 Thadagam Road, Edayarpalayam, Coimbatore',     '641025', 'NONE',     'LARGE',     'WEEKLY',  '2026-10-16', 'ACTIVE')
)

function Get-Xsrf {
    $cookie = $session.Cookies.GetCookies([Uri]$BaseUrl) | Where-Object Name -eq 'XSRF-TOKEN'
    if ($cookie) { [Uri]::UnescapeDataString($cookie.Value) } else { '' }
}

function Invoke-Api([string]$Method, [string]$Path, $Body) {
    $request = @{ Uri = "$BaseUrl$Path"; Method = $Method; WebSession = $session; Headers = @{ 'X-XSRF-TOKEN' = (Get-Xsrf) } }
    if ($null -ne $Body) { $request.ContentType = 'application/json; charset=utf-8'; $request.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5)) }
    Invoke-RestMethod @request
}

function Get-ErrorText($err) {
    if ($err.ErrorDetails -and $err.ErrorDetails.Message) {
        try { return ($err.ErrorDetails.Message | ConvertFrom-Json).detail } catch { return $err.ErrorDetails.Message }
    }
    $err.Exception.Message
}

try { Invoke-Api GET '/api/auth/state' $null | Out-Null }
catch { throw "Cannot reach the Fitzobit app at $BaseUrl. Start it first (or pass -BaseUrl). $($_.Exception.Message)" }

if (-not $Credential) {
    $userName = Read-Host 'Fitzobit user name'
    $securePassword = Read-Host 'Password' -AsSecureString
    $Credential = New-Object pscredential ($userName, $securePassword)
}
try { Invoke-Api POST '/api/auth/login' @{ userName = $Credential.UserName; password = $Credential.GetNetworkCredential().Password } | Out-Null }
catch { throw "Sign-in failed: $(Get-ErrorText $_)" }
Invoke-Api GET '/api/auth/state' $null | Out-Null   # fresh CSRF token for the signed-in user

$dictionaries = Invoke-Api GET '/api/dictionaries' $null
function Get-EntryId($list, [string]$code, [string]$what) {
    $entry = $list | Where-Object code -eq $code | Select-Object -First 1
    if (-not $entry) { throw "The $what '$code' is missing or inactive (Settings -> Dropdown values)." }
    $entry.id
}

$added = 0; $skipped = 0; $failed = 0
foreach ($c in $customers) {
    $name, $phone, $address, $pincode, $preference, $mealType, $frequency, $start, $status = $c
    $existing = Invoke-Api GET "/api/customers?search=$phone&pageSize=10" $null
    if ($existing.total -gt 0) { Write-Host "  skipped  $name (phone $phone already exists)" -ForegroundColor DarkGray; $skipped++; continue }
    $package = @{
        preferenceId = Get-EntryId $dictionaries.preference $preference 'preference'
        mealTypeId = Get-EntryId $dictionaries.mealType $mealType 'meal type'
        quantity = 1
        frequencyId = Get-EntryId $dictionaries.frequency $frequency 'frequency'
        startDate = $start
    }
    $body = @{
        customerName = $name; phoneNumber = $phone; address = $address; pincode = $pincode
        preferredDeliveryTime = '07:30:00'
        packages = @($package)
        statusId = Get-EntryId $dictionaries.customerStatus $status 'status'
    }
    try {
        $result = Invoke-Api POST '/api/customers' $body
        Write-Host ("  added    {0}  {1,-17} ends {2}" -f $result.customerId, $name, $result.packages[0].endDate) -ForegroundColor Green
        $added++
    }
    catch { Write-Host "  FAILED   $name : $(Get-ErrorText $_)" -ForegroundColor Red; $failed++ }
}

Write-Host ''
Write-Host "Done: $added added, $skipped skipped, $failed failed."
$area = Invoke-Api GET '/api/settings/delivery-area' $null
if (-not $area.isConfigured) {
    Write-Host 'Locations are looked up only after the service city is set: Settings -> General -> Delivery area -> Set city -> Coimbatore.' -ForegroundColor Yellow
} else {
    Write-Host "Locations are being looked up in the background (about 1 per second); open Delivery Classification in a minute."
}
