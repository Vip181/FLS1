<#
    menu-demarrage.ps1  -  File System OS Company (Vincent Senut)

    Installe un MENU DE DEMARRAGE sur la partition EFI du disque :
        File System OS
        Windows
    Le menu est affiche par le chargeur Limine, avec un compte a rebours de 10 secondes.

    A LANCER DANS WINDOWS, dans PowerShell EN ADMINISTRATEUR :
        powershell -ExecutionPolicy Bypass -File menu-demarrage.ps1
        powershell -ExecutionPolicy Bypass -File menu-demarrage.ps1 -Iso C:\chemin\FileSystemOS.iso
    Pour tout retirer :
        powershell -ExecutionPolicy Bypass -File menu-demarrage.ps1 -Retirer

    Securite :
      - le dossier Microsoft de la partition EFI n'est JAMAIS modifie ;
      - la partition EFI est sauvegardee dans Documents avant toute modification ;
      - l'entree "Windows Boot Manager" reste dans le BIOS : touche F12 au demarrage pour
        demarrer Windows directement si quelque chose ne va pas ;
      - le script refuse de continuer si BitLocker est actif (sinon Windows demanderait la
        cle de recuperation a chaque demarrage).
#>
param(
    [string]$Iso = "",
    [switch]$Retirer
)

$ErrorActionPreference = "Stop"
$Nom = "File System OS (menu)"
$Marque = "INSTALLE-PAR-FILESYSTEMOS.txt"

function Etape($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)    { Write-Host "   $t" -ForegroundColor Green }
function Info($t)  { Write-Host "   $t" }
function Stop-Erreur($t) { Write-Host ""; Write-Host "ERREUR : $t" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------
# 0. Verifications
# ---------------------------------------------------------------------------
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
          [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Stop-Erreur "lancez PowerShell EN ADMINISTRATEUR (clic droit > Executer en tant qu'administrateur)." }

if (-not (Test-Path "$env:windir\System32\bcdedit.exe")) { Stop-Erreur "bcdedit introuvable." }
try { $fw = (Get-ItemProperty "HKLM:\System\CurrentControlSet\Control" -Name PEFirmwareType).PEFirmwareType } catch { $fw = 0 }
if ($fw -ne 2) {
    Stop-Erreur "Windows ne demarre pas en mode UEFI. Ce menu ne fonctionne qu'en UEFI.`n         (Pour File System OS en mode Legacy, utilisez la touche F12 et la cle USB.)"
}

# Choisit une lettre libre pour la partition EFI
function Lettre-Libre {
    $prises = (Get-PSDrive -PSProvider FileSystem).Name
    foreach ($l in @("S","T","U","V","W","Y","Z")) { if ($prises -notcontains $l) { return $l } }
    Stop-Erreur "aucune lettre de lecteur libre."
}

function Monter-EFI($l) {
    & mountvol "$($l):" /S | Out-Null
    if (-not (Test-Path "$($l):\EFI")) { Stop-Erreur "impossible de monter la partition EFI." }
}
function Demonter-EFI($l) { & mountvol "$($l):" /D | Out-Null }

# Entrees de micrologiciel creees par ce script (on les retrouve par leur description)
function Entrees-FSOS {
    $sortie = (& bcdedit /enum firmware) -join "`n"
    $blocs = $sortie -split "`n\s*`n"
    $ids = @()
    foreach ($b in $blocs) {
        if ($b -match [regex]::Escape($Nom)) {
            $m = [regex]::Match($b, "\{[0-9a-fA-F\-]{36}\}")
            if ($m.Success) { $ids += $m.Value }
        }
    }
    return $ids
}

# ---------------------------------------------------------------------------
# Mode -Retirer : on enleve tout ce que ce script a installe, et rien d'autre
# ---------------------------------------------------------------------------
if ($Retirer) {
    Etape "Retrait du menu File System OS"
    foreach ($id in Entrees-FSOS) { & bcdedit /delete $id | Out-Null; Ok "entree du BIOS supprimee : $id" }
    $L = Lettre-Libre
    Monter-EFI $L
    try {
        if (Test-Path "$($L):\fsos\$Marque") {
            Remove-Item "$($L):\EFI\fsos" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$($L):\fsos" -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path "$($L):\boot\limine\limine.conf") {
                if ((Get-Content "$($L):\boot\limine\limine.conf" -Raw) -match "File System OS") {
                    Remove-Item "$($L):\boot\limine\limine.conf" -Force
                }
            }
            Ok "fichiers File System OS retires de la partition EFI"
        } else { Info "rien a retirer sur la partition EFI" }
    } finally { Demonter-EFI $L }
    Ok "Termine. Le PC demarrera de nouveau directement sur Windows."
    exit 0
}

# ---------------------------------------------------------------------------
# 1. BitLocker : doit etre suspendu
# ---------------------------------------------------------------------------
Etape "1/7  BitLocker"
try {
    $bl = Get-BitLockerVolume -MountPoint "C:" -ErrorAction Stop
    if ($bl.ProtectionStatus -eq "On") {
        Stop-Erreur "BitLocker protege C:. Un autre chargeur devant Windows declencherait la demande de`n         cle de recuperation a chaque demarrage. Suspendez-le d'abord :`n             Suspend-BitLocker -MountPoint C: -RebootCount 0`n         et notez votre cle de recuperation (compte Microsoft ou impression)."
    }
    Ok "BitLocker inactif ou suspendu"
} catch { Ok "BitLocker non utilise" }

# ---------------------------------------------------------------------------
# 2. Noyau de File System OS (depuis l'ISO)
# ---------------------------------------------------------------------------
Etape "2/7  Noyau de File System OS"
if ($Iso -eq "") {
    $racine = Split-Path $PSScriptRoot -Parent
    $trouvee = Get-ChildItem -Path $racine -Recurse -Filter *.iso -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($trouvee -eq $null) { Stop-Erreur "aucune ISO trouvee. Compilez le projet, ou indiquez -Iso C:\...\FileSystemOS.iso" }
    $Iso = $trouvee.FullName
}
if (-not (Test-Path $Iso)) { Stop-Erreur "ISO introuvable : $Iso" }
Info "ISO : $Iso"

$tmp = Join-Path $env:TEMP "fsos-menu"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $tmp | Out-Null

$IsoPlein = (Resolve-Path $Iso).Path
$img = Mount-DiskImage -ImagePath $IsoPlein -PassThru
try {
    Start-Sleep -Seconds 2
    $lettreIso = ($img | Get-Volume).DriveLetter
    if (-not $lettreIso) { Stop-Erreur "l'ISO n'a pas recu de lettre de lecteur." }
    $noyau = Get-ChildItem "$($lettreIso):\" -Recurse -Include *.bin,*.elf -ErrorAction SilentlyContinue |
             Where-Object { $_.Length -gt 100KB } | Sort-Object Length | Select-Object -Last 1
    if ($noyau -eq $null) { Stop-Erreur "noyau introuvable dans l'ISO." }
    Copy-Item $noyau.FullName "$tmp\FileSystemOS.bin"
} finally { Dismount-DiskImage -ImagePath $IsoPlein | Out-Null }
Ok "noyau : $($noyau.Name) ($([math]::Round($noyau.Length / 1MB, 1)) Mo)"

# Multiboot 1 ou 2 ? On cherche la signature dans les 32 premiers Ko
$octets = [System.IO.File]::ReadAllBytes("$tmp\FileSystemOS.bin")
$proto = "multiboot1"
for ($i = 0; $i -lt [math]::Min(32768, $octets.Length - 4); $i += 4) {
    $v = [BitConverter]::ToUInt32($octets, $i)
    if ($v -eq 0xE85250D6 -and ($i % 8) -eq 0) { $proto = "multiboot2"; break }
    if ($v -eq 0x1BADB002) { $proto = "multiboot1"; break }
}
Ok "protocole : $proto"

# ---------------------------------------------------------------------------
# 3. Chargeur Limine (version UEFI)
# ---------------------------------------------------------------------------
Etape "3/7  Chargeur Limine"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$url = "https://github.com/limine-bootloader/limine/raw/v8.x-binary/BOOTX64.EFI"
try { Invoke-WebRequest -Uri $url -OutFile "$tmp\BOOTX64.EFI" -UseBasicParsing }
catch { Stop-Erreur "telechargement de Limine impossible ($url). Verifiez la connexion internet." }
$efi = [System.IO.File]::ReadAllBytes("$tmp\BOOTX64.EFI")
if ($efi.Length -lt 100KB -or $efi[0] -ne 0x4D -or $efi[1] -ne 0x5A) { Stop-Erreur "le fichier Limine telecharge n'est pas un programme EFI valide." }
Ok "Limine telecharge ($([math]::Round($efi.Length / 1KB)) Ko)"

# ---------------------------------------------------------------------------
# 4. Partition EFI : montage, verification, sauvegarde
# ---------------------------------------------------------------------------
Etape "4/7  Partition EFI"
$L = Lettre-Libre
Monter-EFI $L
try {
    $windows = Test-Path "$($L):\EFI\Microsoft\Boot\bootmgfw.efi"
    if ($windows) { Ok "Windows trouve : \EFI\Microsoft\Boot\bootmgfw.efi" }
    else { Info "ATTENTION : chargeur de Windows introuvable, le menu n'aura pas d'entree Windows." }

    $libre = ([System.IO.DriveInfo]::new($L)).AvailableFreeSpace
    $besoin = (Get-Item "$tmp\FileSystemOS.bin").Length + $efi.Length + 1MB
    if ($libre -lt $besoin) { Stop-Erreur "partition EFI trop pleine ($([math]::Round($libre/1MB)) Mo libres)." }
    Ok "$([math]::Round($libre/1MB)) Mo libres"

    $sauvegarde = Join-Path ([Environment]::GetFolderPath("MyDocuments")) ("Sauvegarde-partition-EFI-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    & robocopy "$($L):\" $sauvegarde /E /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { Stop-Erreur "sauvegarde de la partition EFI impossible." }
    Ok "sauvegarde : $sauvegarde"

    # -----------------------------------------------------------------------
    # 5. Fichiers du menu (rien n'est ecrit dans \EFI\Microsoft)
    # -----------------------------------------------------------------------
    Etape "5/7  Installation des fichiers"
    New-Item -ItemType Directory "$($L):\EFI\fsos" -Force | Out-Null
    New-Item -ItemType Directory "$($L):\fsos" -Force | Out-Null
    New-Item -ItemType Directory "$($L):\boot\limine" -Force | Out-Null
    Copy-Item "$tmp\BOOTX64.EFI" "$($L):\EFI\fsos\BOOTX64.EFI" -Force
    Copy-Item "$tmp\FileSystemOS.bin" "$($L):\fsos\FileSystemOS.bin" -Force
    Set-Content "$($L):\fsos\$Marque" "Menu de demarrage installe par File System OS le $(Get-Date)" -Encoding ASCII

    $conf = @"
timeout: 10
default_entry: 1
remember_last_entry: yes
interface_branding: File System OS Company - menu de demarrage

/File System OS
    comment: Systeme d'exploitation de Vincent Senut
    protocol: $proto
    path: boot():/fsos/FileSystemOS.bin
    resolution: 1024x768x32
"@
    if ($windows) {
        $conf += @"


/Windows
    comment: Gestionnaire de demarrage de Windows
    protocol: efi
    path: boot():/EFI/Microsoft/Boot/bootmgfw.efi
"@
    }
    # Limine cherche sa configuration a ces emplacements de la partition de demarrage
    Set-Content "$($L):\boot\limine\limine.conf" $conf -Encoding ASCII
    Set-Content "$($L):\EFI\fsos\limine.conf" $conf -Encoding ASCII
    Ok "chargeur, noyau et menu copies"
} finally { Demonter-EFI $L }

# ---------------------------------------------------------------------------
# 6. Entree dans le micrologiciel (BIOS UEFI), placee en premier
# ---------------------------------------------------------------------------
Etape "6/7  Entree de demarrage"
foreach ($ancien in Entrees-FSOS) { & bcdedit /delete $ancien | Out-Null }
$copie = (& bcdedit /copy "{bootmgr}" /d $Nom) -join " "
$m = [regex]::Match($copie, "\{[0-9a-fA-F\-]{36}\}")
if (-not $m.Success) { Stop-Erreur "creation de l'entree impossible : $copie" }
$id = $m.Value
& bcdedit /set $id path "\EFI\fsos\BOOTX64.EFI" | Out-Null
& bcdedit /set "{fwbootmgr}" displayorder $id /addfirst | Out-Null
Ok "entree '$Nom' creee et placee en premier : $id"

# ---------------------------------------------------------------------------
# 7. Fin
# ---------------------------------------------------------------------------
Etape "7/7  Termine"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Info "Au prochain demarrage, un menu propose File System OS et Windows (10 secondes)."
Info "Le dernier choix est retenu."
Info ""
Info "Dans le BIOS (F2) : Boot List Option = UEFI, Secure Boot = Desactive."
Info "En cas de probleme : touche F12 au demarrage > Windows Boot Manager."
Info "Pour tout retirer : powershell -ExecutionPolicy Bypass -File menu-demarrage.ps1 -Retirer"
