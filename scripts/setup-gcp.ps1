<#
.SYNOPSIS
    SixJars 部署的一次性 GCP 設定（ADR 0012、docs/deploy.md §7.1）。

.DESCRIPTION
    建立 Artifact Registry、runtime／deployer 兩個 service account、IAM 授權與 GitHub Actions 用的
    Workload Identity Federation，最後印出要填進 GitHub repository variables 的值。

    前提：已完成 docs/deploy.md 第 1–6 節（GCP 專案與 billing、Neon、migration、OAuth client，
    以及 Secret Manager 裡的 sixjars-db 與 sixjars-google-client-secret）。

    可以重複執行：已存在的資源會略過，IAM 授權重複加入不會出錯。
    腳本不讀、不寫任何 secret 的值。

.PARAMETER ProjectId
    GCP 專案 ID（不是專案編號）。

.PARAMETER GitHubRepo
    owner/repo，WIF 只信任這個 repo。

.PARAMETER SetGitHubVariables
    一併用 gh CLI 設定 repository variables 與 production environment（需要已 gh auth login）。
    MEDIATR_LICENSE_KEY 是 secret，腳本只會提示你自己執行 gh secret set，不經手它的值。

.PARAMETER GoogleClientId
    搭配 -SetGitHubVariables：寫入 GOOGLE_CLIENT_ID variable 的 OAuth client ID。

.PARAMETER RemoveComputeSecretAccess
    移除舊版 docs/deploy.md §6 授予 Compute Engine default service account 的 secret 讀取權。

.EXAMPLE
    ./scripts/setup-gcp.ps1 -ProjectId my-sixjars

.EXAMPLE
    ./scripts/setup-gcp.ps1 -ProjectId my-sixjars -SetGitHubVariables -GoogleClientId 123-abc.apps.googleusercontent.com -RemoveComputeSecretAccess
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectId,
    [string] $GitHubRepo = 'AdamChang/SixJars-AccountingBook',
    [string] $Region = 'asia-east1',
    [switch] $SetGitHubVariables,
    [string] $GoogleClientId,
    [switch] $RemoveComputeSecretAccess
)

$ErrorActionPreference = 'Stop'

$ArRepository = 'sixjars'
$RuntimeSaName = 'sixjars-run'
$DeployerSaName = 'sixjars-deploy'
$PoolId = 'github'
$ProviderId = 'github-actions'
$Secrets = @('sixjars-db', 'sixjars-google-client-secret')

function Step([string] $Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }

# 執行 gcloud，失敗時中止腳本。
function Invoke-Gcloud {
    & gcloud @args
    if ($LASTEXITCODE -ne 0) { throw "gcloud $($args -join ' ') 失敗（exit code $LASTEXITCODE）" }
}

# 只判斷 gcloud 指令成不成功（用來檢查資源是否已存在），輸出一律丟掉。
# Windows PowerShell 5.1 在 Stop 模式下會把原生指令的 stderr 當成例外，這裡暫時改回 Continue。
function Test-Gcloud {
    $ErrorActionPreference = 'Continue'
    & gcloud @args *> $null
    return $LASTEXITCODE -eq 0
}

if ($SetGitHubVariables -and -not $GoogleClientId) {
    throw '-SetGitHubVariables 需要同時提供 -GoogleClientId。'
}
if (-not (Get-Command gcloud -ErrorAction SilentlyContinue)) {
    throw '找不到 gcloud，請先安裝 Google Cloud SDK 並 gcloud auth login。'
}

Step "專案 $ProjectId"
Invoke-Gcloud config set project $ProjectId
$ProjectNumber = (& gcloud projects describe $ProjectId --format='value(projectNumber)')
if ($LASTEXITCODE -ne 0 -or -not $ProjectNumber) { throw "讀不到專案 $ProjectId 的專案編號。" }
$RuntimeSa = "$RuntimeSaName@$ProjectId.iam.gserviceaccount.com"
$DeployerSa = "$DeployerSaName@$ProjectId.iam.gserviceaccount.com"

Step '啟用 API'
Invoke-Gcloud services enable `
    run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com `
    iam.googleapis.com iamcredentials.googleapis.com sts.googleapis.com cloudresourcemanager.googleapis.com

Step '確認 Secret Manager 已有兩個 secret'
foreach ($secret in $Secrets) {
    if (-not (Test-Gcloud secrets describe $secret)) {
        throw "找不到 secret $secret：請先依 docs/deploy.md §6 建立。"
    }
}

Step "Artifact Registry：$Region/$ArRepository（只保留最近 3 個版本）"
if (-not (Test-Gcloud artifacts repositories describe $ArRepository --location $Region)) {
    Invoke-Gcloud artifacts repositories create $ArRepository `
        --repository-format docker --location $Region --description 'SixJars API images'
}
# Keep 優先於 Delete：所有版本都符合刪除條件，只有最近 3 個被保留。cleanup 由 GCP 每天背景執行，不是 push 當下。
$policyFile = Join-Path ([System.IO.Path]::GetTempPath()) 'sixjars-ar-cleanup.json'
@'
[
  { "name": "delete-all", "action": { "type": "Delete" }, "condition": { "tagState": "any" } },
  { "name": "keep-latest-3", "action": { "type": "Keep" }, "mostRecentVersions": { "keepCount": 3 } }
]
'@ | Set-Content -Path $policyFile -Encoding ascii
try {
    Invoke-Gcloud artifacts repositories set-cleanup-policies $ArRepository `
        --location $Region --policy $policyFile --no-dry-run
}
finally {
    Remove-Item $policyFile -ErrorAction SilentlyContinue
}

Step 'Service accounts'
if (-not (Test-Gcloud iam service-accounts describe $RuntimeSa)) {
    Invoke-Gcloud iam service-accounts create $RuntimeSaName --display-name 'SixJars Cloud Run runtime'
}
if (-not (Test-Gcloud iam service-accounts describe $DeployerSa)) {
    Invoke-Gcloud iam service-accounts create $DeployerSaName --display-name 'SixJars GitHub Actions deployer'
}

Step "runtime SA：只能讀 $($Secrets -join '、')"
foreach ($secret in $Secrets) {
    Invoke-Gcloud secrets add-iam-policy-binding $secret `
        --member "serviceAccount:$RuntimeSa" --role roles/secretmanager.secretAccessor --condition None --quiet | Out-Null
}

if ($RemoveComputeSecretAccess) {
    Step '移除 Compute Engine default service account 的 secret 讀取權'
    $computeSa = "$ProjectNumber-compute@developer.gserviceaccount.com"
    foreach ($secret in $Secrets) {
        if (Test-Gcloud secrets remove-iam-policy-binding $secret `
                --member "serviceAccount:$computeSa" --role roles/secretmanager.secretAccessor --quiet) {
            Write-Host "  已移除 $secret"
        }
        else {
            Write-Host "  $secret 原本就沒有授權給 $computeSa"
        }
    }
}

Step 'deployer SA：部署 Cloud Run、推 image、以 runtime SA 身分部署'
Invoke-Gcloud projects add-iam-policy-binding $ProjectId `
    --member "serviceAccount:$DeployerSa" --role roles/run.admin --condition None --quiet | Out-Null
Invoke-Gcloud artifacts repositories add-iam-policy-binding $ArRepository --location $Region `
    --member "serviceAccount:$DeployerSa" --role roles/artifactregistry.writer --quiet | Out-Null
Invoke-Gcloud iam service-accounts add-iam-policy-binding $RuntimeSa `
    --member "serviceAccount:$DeployerSa" --role roles/iam.serviceAccountUser --quiet | Out-Null

Step "Workload Identity Federation：只信任 $GitHubRepo 的 production environment、v* tag"
if (-not (Test-Gcloud iam workload-identity-pools describe $PoolId --location global)) {
    Invoke-Gcloud iam workload-identity-pools create $PoolId --location global --display-name 'GitHub Actions'
}
$condition = "assertion.repository == '$GitHubRepo' && assertion.environment == 'production' && assertion.ref.startsWith('refs/tags/v')"
$mapping = 'google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref,attribute.environment=assertion.environment'
if (-not (Test-Gcloud iam workload-identity-pools providers describe $ProviderId --location global --workload-identity-pool $PoolId)) {
    Invoke-Gcloud iam workload-identity-pools providers create-oidc $ProviderId `
        --location global --workload-identity-pool $PoolId --display-name 'GitHub Actions OIDC' `
        --issuer-uri 'https://token.actions.githubusercontent.com' `
        --attribute-mapping $mapping --attribute-condition $condition
}
else {
    # 重跑時以腳本為準，避免 console 手改的條件放寬了信任範圍。
    Invoke-Gcloud iam workload-identity-pools providers update-oidc $ProviderId `
        --location global --workload-identity-pool $PoolId `
        --attribute-mapping $mapping --attribute-condition $condition
}
$poolResource = "projects/$ProjectNumber/locations/global/workloadIdentityPools/$PoolId"
Invoke-Gcloud iam service-accounts add-iam-policy-binding $DeployerSa `
    --member "principalSet://iam.googleapis.com/$poolResource/attribute.repository/$GitHubRepo" `
    --role roles/iam.workloadIdentityUser --quiet | Out-Null
$WifProvider = "$poolResource/providers/$ProviderId"

if ($SetGitHubVariables) {
    Step "GitHub：$GitHubRepo 的 repository variables 與 production environment"
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '找不到 gh CLI。' }
    $variables = [ordered]@{
        GCP_PROJECT_ID   = $ProjectId
        GCP_WIF_PROVIDER = $WifProvider
        GCP_DEPLOYER_SA  = $DeployerSa
        GOOGLE_CLIENT_ID = $GoogleClientId
    }
    foreach ($name in $variables.Keys) {
        & gh variable set $name --repo $GitHubRepo --body $variables[$name]
        if ($LASTEXITCODE -ne 0) { throw "gh variable set $name 失敗。" }
    }
    # production environment 只允許 v* tag 部署（deployment protection rule）。
    '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' |
        & gh api --method PUT "repos/$GitHubRepo/environments/production" --input - | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '建立 production environment 失敗。' }
    $policies = & gh api "repos/$GitHubRepo/environments/production/deployment-branch-policies" | ConvertFrom-Json
    if (-not ($policies.branch_policies | Where-Object { $_.name -eq 'v*' -and $_.type -eq 'tag' })) {
        & gh api --method POST "repos/$GitHubRepo/environments/production/deployment-branch-policies" `
            -f name='v*' -f type=tag | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '設定 production environment 的 tag 規則失敗。' }
    }
}

Step '完成'
Write-Host @"

GitHub repository variables（Settings → Secrets and variables → Actions → Variables）：
  GCP_PROJECT_ID    = $ProjectId
  GCP_WIF_PROVIDER  = $WifProvider
  GCP_DEPLOYER_SA   = $DeployerSa
  GOOGLE_CLIENT_ID  = <OAuth client ID>

GitHub repository secret（Settings → Secrets and variables → Actions → Secrets）：
  MEDIATR_LICENSE_KEY = <MediatR Community license key>
  或執行：gh secret set MEDIATR_LICENSE_KEY --repo $GitHubRepo   （會提示你貼上，值不會留在命令列歷史）

GitHub environment：production，Deployment branches and tags 只允許 tag 規則 v*。

Cloud Run 網址在第一次部署後確定；把 https://<網址>/auth/callback 加進 OAuth client 的 redirect URI。
"@
