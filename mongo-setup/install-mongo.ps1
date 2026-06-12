<#
.SYNOPSIS
  Install and configure MongoDB 7.0 on VM2 (the BMT MongoDB-on-VM backend) per project spec.

.DESCRIPTION
  Realized Windows equivalent of the deferred install.sh. The provisioned VM2 in this
  environment is Windows Server 2025 (32 vCPU / 256 GB RAM / 512 GB premium SSD data disk),
  so the Linux steps from the spec are executed via their Windows equivalents:

    spec (Linux)                    -> realized (Windows)
    --------------------------------------------------------------
    ext4 + /etc/fstab @ /mnt/data   -> NTFS volume on the data disk (default E:)
    mongodb:mongodb owner           -> NT AUTHORITY\NetworkService service account
    apt mongodb-org 7.0             -> official Windows MSI (resolved from downloads.mongodb.org)
    systemd unit                    -> Windows Service "MongoDB" (auto-start)
    ufw allow 27017                 -> Windows Defender Firewall inbound rule
    /etc/mongod.conf                -> C:\Program Files\MongoDB\Server\7.0\bin\mongod.cfg

  Idempotent and guarded:
    - Only formats the data disk if it is RAW/empty and ~512 GB. Never touches the OS disk.
    - WiredTiger cache is left at default (~50% RAM). cacheSizeGB is never set.
    - Creates bmt_db + calc_input + calc_output with the DEFAULT _id index ONLY
      (drops any stray secondary index). A ReqId field is NOT indexed by design.
    - Enables the slow-query profiler on bmt_db: level 1, slowms 50.

  Secrets: strong random passwords are generated for the admin and bench users and written
  to a git-ignored file (default infra\connection-strings.json). Nothing secret is printed
  to stdout except a one-time redactable summary.

.NOTES
  Run from an elevated PowerShell on VM2. The Azure NSG rule is managed separately by infra;
  this script only configures the host firewall.
#>
[CmdletBinding()]
param(
  [string] $DataDriveLetter = 'E',
  [string] $DbName          = 'bmt_db',
  [string] $BenchUser       = 'bmt_bench',
  [string] $AdminUser       = 'bmt_admin',
  [int]    $Port            = 27017,
  [string] $ReplSetName     = 'rs0',
  # Private subnet(s) allowed to reach 27017 on the host firewall (VM1's subnet).
  [string] $AllowedSubnet   = '10.3.0.0/24',
  # Member host used in rs.initiate so external clients (VM1) can connect.
  # Leave empty to auto-detect the private IPv4 address.
  [string] $MemberHost      = '',
  # Where to persist the generated connection strings + secrets (git-ignored).
  [string] $SecretsOutFile  = ''
)

$ErrorActionPreference = 'Stop'
function Fail($msg) { Write-Host "FAILED: $msg" -ForegroundColor Red; exit 1 }

$dataRoot = "${DataDriveLetter}:\mongo"
$dataPath = "$dataRoot\data"
$logPath  = "$dataRoot\log"
$keyFile  = "$dataRoot\keyfile"
$work     = 'C:\setup'
New-Item -ItemType Directory -Path $work -Force | Out-Null

# ---------------------------------------------------------------------------
# 1. Inventory (read-only)
# ---------------------------------------------------------------------------
Write-Host '=== 1. Inventory ===' -ForegroundColor Cyan
"OS    : $((Get-CimInstance Win32_OperatingSystem).Caption)"
"Host  : $env:COMPUTERNAME"
"CPUs  : $((Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors)"
"RAMGB : $([math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1))"
Get-Disk | Format-Table Number, FriendlyName, @{n='SizeGB';e={[math]::Round($_.Size/1GB,1)}}, PartitionStyle, OperationalStatus -AutoSize | Out-String -Width 200

# ---------------------------------------------------------------------------
# 2. Prepare the data disk (only if not already present as the target volume)
# ---------------------------------------------------------------------------
Write-Host '=== 2. Data disk ===' -ForegroundColor Cyan
$targetVol = Get-Volume -DriveLetter $DataDriveLetter -ErrorAction SilentlyContinue
if (-not $targetVol) {
  $disk = Get-Disk | Where-Object { $_.Number -ne 0 -and $_.PartitionStyle -eq 'RAW' -and $_.Size -ge 500GB -and $_.Size -le 520GB } | Select-Object -First 1
  if (-not $disk) { Fail "No empty ~512 GB data disk found to format (and ${DataDriveLetter}: does not exist)." }
  if ($disk.Number -eq 0) { Fail 'Refusing to touch the OS disk (disk 0).' }
  Initialize-Disk -Number $disk.Number -PartitionStyle GPT
  New-Partition -DiskNumber $disk.Number -UseMaximumSize -DriveLetter $DataDriveLetter | Out-Null
  Format-Volume -DriveLetter $DataDriveLetter -FileSystem NTFS -NewFileSystemLabel 'mongodata' -Confirm:$false | Out-Null
  Write-Host "Formatted disk $($disk.Number) as ${DataDriveLetter}: (NTFS)"
} else {
  Write-Host "${DataDriveLetter}: already present ($([math]::Round($targetVol.Size/1GB,1)) GB) — leaving as-is."
}
New-Item -ItemType Directory -Path $dataPath, $logPath -Force | Out-Null

# ---------------------------------------------------------------------------
# 3. Install MongoDB 7.0 server (MSI) + mongosh, if missing
# ---------------------------------------------------------------------------
Write-Host '=== 3. Install MongoDB 7.0 + mongosh ===' -ForegroundColor Cyan
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$mongod = Get-ChildItem 'C:\Program Files\MongoDB\Server\7.0\bin\mongod.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $mongod) {
  $full = Invoke-RestMethod -Uri 'https://downloads.mongodb.org/full.json' -UseBasicParsing
  $ver  = $full.versions |
            Where-Object { $_.version -match '^7\.0\.\d+$' } |
            Sort-Object { [version]$_.version } -Descending | Select-Object -First 1
  $msiUrl = ($ver.downloads | Where-Object { $_.target -eq 'windows' -and $_.edition -eq 'base' -and $_.arch -eq 'x86_64' } | Select-Object -First 1).msi
  if (-not $msiUrl) { Fail 'Could not resolve a MongoDB 7.0 Windows MSI URL.' }
  $msi = "$work\mongodb-server-7.0.msi"
  Invoke-WebRequest -Uri $msiUrl -OutFile $msi -UseBasicParsing
  $p = Start-Process msiexec.exe -ArgumentList @('/i', "`"$msi`"", '/qn', '/norestart',
        'ADDLOCAL=ServerService,Client', 'SHOULD_INSTALL_COMPASS=0',
        "MONGO_DATA_PATH=$dataPath", "MONGO_LOG_PATH=$logPath") -Wait -PassThru
  if ($p.ExitCode -ne 0) { Fail "MongoDB MSI install failed (exit $($p.ExitCode))." }
  $mongod = Get-ChildItem 'C:\Program Files\MongoDB\Server\7.0\bin\mongod.exe' | Select-Object -First 1
}
Write-Host "mongod: $($mongod.FullName) ($(& $mongod.FullName --version | Select-Object -First 1))"

$mongosh = (Get-Command mongosh -ErrorAction SilentlyContinue).Source
if (-not $mongosh) { $mongosh = (Get-ChildItem "$work\mongosh" -Recurse -Filter mongosh.exe -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
if (-not $mongosh) {
  $rel   = Invoke-RestMethod -Uri 'https://api.github.com/repos/mongodb-js/mongosh/releases/latest' -Headers @{ 'User-Agent'='setup-agent' } -UseBasicParsing
  $asset = $rel.assets | Where-Object { $_.name -match 'win32-x64\.zip$' } | Select-Object -First 1
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile "$work\mongosh.zip" -UseBasicParsing
  Expand-Archive -Path "$work\mongosh.zip" -DestinationPath "$work\mongosh" -Force
  $mongosh = (Get-ChildItem "$work\mongosh" -Recurse -Filter mongosh.exe | Select-Object -First 1).FullName
}
if (-not $mongosh) { Fail 'mongosh not available after install.' }
Write-Host "mongosh: $mongosh ($(& $mongosh --version))"

# ---------------------------------------------------------------------------
# 4. Replica-set keyfile + bootstrap config (no auth yet) + start + rs.initiate
# ---------------------------------------------------------------------------
Write-Host '=== 4. Bootstrap config + rs.initiate ===' -ForegroundColor Cyan
if (-not $MemberHost) {
  $MemberHost = (Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1).IPv4Address.IPAddress
  if (-not $MemberHost) { Fail 'Could not auto-detect a private IPv4 address; pass -MemberHost.' }
}
Write-Host "Replica-set member host: ${MemberHost}:$Port"

if (-not (Test-Path $keyFile)) {
  $bytes = New-Object byte[] 756
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  Set-Content -Path $keyFile -Value ([Convert]::ToBase64String($bytes)) -NoNewline -Encoding ascii
  icacls $keyFile /inheritance:r /grant 'NT AUTHORITY\NetworkService:(R)' /grant 'BUILTIN\Administrators:(F)' | Out-Null
}

$cfgPath = 'C:\Program Files\MongoDB\Server\7.0\bin\mongod.cfg'
$bootstrap = @"
storage:
  dbPath: $dataPath
systemLog:
  destination: file
  logAppend: true
  path: $logPath\mongod.log
net:
  port: $Port
  bindIp: 0.0.0.0
  maxIncomingConnections: 5000
replication:
  replSetName: $ReplSetName
"@
Set-Content -Path $cfgPath -Value $bootstrap -Encoding ascii

if (-not (Get-Service MongoDB -ErrorAction SilentlyContinue)) {
  & $mongod.FullName --config $cfgPath --install --serviceName MongoDB | Out-Null
}
Restart-Service MongoDB
Start-Sleep -Seconds 4
if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) { Fail "mongod is not listening on $Port." }

$initRes = & $mongosh --quiet --port $Port --eval "try{ rs.initiate({_id:'$ReplSetName', members:[{_id:0, host:'${MemberHost}:$Port'}]}); print('initiated') }catch(e){ print(String(e).indexOf('already initialized')>=0 ? 'already-initialized' : 'ERR:'+e) }"
if ($initRes -match '^ERR:') { Fail "rs.initiate failed: $initRes" }

# 5. Wait for PRIMARY
Write-Host '=== 5. Wait for PRIMARY ===' -ForegroundColor Cyan
$primary = $false
for ($i = 0; $i -lt 30; $i++) {
  $state = (& $mongosh --quiet --port $Port --eval "try{print(rs.status().myState)}catch(e){print(-1)}") -as [int]
  if ($state -eq 1) { $primary = $true; break }
  Start-Sleep -Seconds 2
}
if (-not $primary) { Fail 'Replica set did not reach PRIMARY (myState=1).' }
Write-Host 'PRIMARY (myState=1).'

# ---------------------------------------------------------------------------
# 6. Create users (via localhost exception) — strong random passwords
# ---------------------------------------------------------------------------
Write-Host '=== 6. Create admin + bench users ===' -ForegroundColor Cyan
function New-Pw([int]$n = 28) {
  $a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~'.ToCharArray()
  $b = New-Object byte[] $n
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  -join ($b | ForEach-Object { $a[$_ % $a.Length] })
}
$adminPw = New-Pw 28
$benchPw = New-Pw 28
$env:ADMIN_PW = $adminPw; $env:BENCH_PW = $benchPw
$mk = & $mongosh --quiet --port $Port --eval @"
const a=process.env.ADMIN_PW, b=process.env.BENCH_PW, admin=db.getSiblingDB('admin');
function up(u,pwd,roles){ try{ admin.createUser({user:u,pwd:pwd,roles:roles}); print('created '+u);}catch(e){ if(String(e).indexOf('already exists')>=0){ admin.updateUser(u,{pwd:pwd,roles:roles}); print('updated '+u);} else { print('ERR:'+e); quit(1);} } }
up('$AdminUser', a, [{role:'root',db:'admin'}]);
up('$BenchUser', b, [{role:'readWrite',db:'$DbName'}]);
"@
Remove-Item Env:ADMIN_PW, Env:BENCH_PW
if ($mk -match 'ERR:') { Fail "user creation failed: $mk" }
Write-Host $mk

# ---------------------------------------------------------------------------
# 7. Enable auth + keyFile, restart, confirm auth
# ---------------------------------------------------------------------------
Write-Host '=== 7. Enable auth + restart ===' -ForegroundColor Cyan
$secured = @"
storage:
  dbPath: $dataPath
systemLog:
  destination: file
  logAppend: true
  path: $logPath\mongod.log
net:
  port: $Port
  bindIp: 0.0.0.0
  maxIncomingConnections: 5000
security:
  authorization: enabled
  keyFile: $keyFile
replication:
  replSetName: $ReplSetName
"@
Set-Content -Path $cfgPath -Value $secured -Encoding ascii
Restart-Service MongoDB
Start-Sleep -Seconds 4
Set-Service MongoDB -StartupType Automatic

$benchConn = "mongodb://${BenchUser}:$([uri]::EscapeDataString($benchPw))@${MemberHost}:$Port/$DbName?replicaSet=$ReplSetName&authSource=admin"
$adminConn = "mongodb://${AdminUser}:$([uri]::EscapeDataString($adminPw))@${MemberHost}:$Port/admin?replicaSet=$ReplSetName&authSource=admin"
$authChk = & $mongosh $benchConn --quiet --eval "print('bench_authed='+db.runCommand({connectionStatus:1}).authInfo.authenticatedUsers[0].user)"
if ($authChk -notmatch 'bench_authed=') { Fail "bench auth check failed: $authChk" }
Write-Host $authChk

# ---------------------------------------------------------------------------
# 8 + 9. Profiler + db/collections + _id-only indexes (as admin)
# ---------------------------------------------------------------------------
Write-Host '=== 8/9. Profiler + bmt_db + _id-only indexes ===' -ForegroundColor Cyan
$mkdb = & $mongosh $adminConn --quiet --eval @"
const t=db.getSiblingDB('$DbName');
['calc_input','calc_output'].forEach(n=>{ if(t.getCollectionNames().indexOf(n)===-1){t.createCollection(n);print('created '+n);} else print(n+' exists'); });
['calc_input','calc_output'].forEach(n=>{ t.getCollection(n).getIndexes().forEach(ix=>{ if(ix.name!=='_id_'){ print('dropping '+n+'.'+ix.name); t.getCollection(n).dropIndex(ix.name);} }); });
t.setProfilingLevel(1,{slowms:50});
print('profiler='+t.getProfilingStatus().was+' slowms='+t.getProfilingStatus().slowms);
"@
Write-Host $mkdb

# ---------------------------------------------------------------------------
# 10. Host firewall (NSG is managed separately by infra)
# ---------------------------------------------------------------------------
Write-Host '=== 10. Host firewall ===' -ForegroundColor Cyan
Get-NetFirewallRule -DisplayName 'MongoDB BMT 27017' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'MongoDB BMT 27017' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress $AllowedSubnet -Profile Any | Out-Null
Write-Host "Inbound TCP $Port allowed from $AllowedSubnet."

# ---------------------------------------------------------------------------
# 11. Verify + persist secrets (git-ignored) + redacted summary
# ---------------------------------------------------------------------------
Write-Host '=== 11. Verify ===' -ForegroundColor Cyan
& $mongosh $adminConn --quiet --eval @"
const t=db.getSiblingDB('$DbName');
printjson({version:db.version(), replicaSetState:rs.status().myState, replicaSetName:rs.status().set,
  profilerLevel:t.getProfilingStatus().was, slowms:t.getProfilingStatus().slowms,
  calc_input:t.calc_input.getIndexes().map(i=>i.name), calc_output:t.calc_output.getIndexes().map(i=>i.name)});
"@

if (-not $SecretsOutFile) {
  $SecretsOutFile = Join-Path (Resolve-Path "$PSScriptRoot\..\infra" -ErrorAction SilentlyContinue) 'connection-strings.json'
  if (-not $SecretsOutFile) { $SecretsOutFile = "$work\connection-strings.json" }
}
@{
  mongoVm    = $benchConn
  mongoVmAdmin = $adminConn
  cosmosRu   = '<set-by-infra>'
  documentDb = '<set-by-infra>'
} | ConvertTo-Json | Set-Content -Path $SecretsOutFile -Encoding ascii
Write-Host ''
Write-Host "Secrets written (git-ignored): $SecretsOutFile" -ForegroundColor Yellow
Write-Host 'Bench connection string (REDACTED here; full value in the file above):'
Write-Host "  mongodb://${BenchUser}:<password>@${MemberHost}:$Port/$DbName?replicaSet=$ReplSetName&authSource=admin"
Write-Host 'Setup complete.' -ForegroundColor Green
