<#
    installer-ssd.ps1  -  File System OS Company (Vincent Senut)

    DEMARRER FILE SYSTEM OS DEPUIS LE SSD, SANS LA CLE USB (mode Legacy du BIOS).

    Tout File System OS (programme de demarrage de Cosmos, noyau, threads, processus,
    multitache, interface graphique, connexion, bureau, programmes) tient dans UN SEUL
    fichier : le noyau, rangé dans l'ISO. Ce script :
      1. copie ce noyau, et le menu de demarrage de l'ISO, sur la partition FSOS (X:\boot) ;
      2. installe GRUB, le MEME chargeur que sur la cle, au debut du SSD, avec sa petite
         partition de demarrage BIOS (1 Mo) creee dans l'espace non alloue ;
      3. sauvegarde d'abord le debut du disque dans Documents, pour pouvoir tout annuler.
    Windows n'est pas modifie : il demarre en UEFI, qui ignore ce que GRUB ecrit.

    A LANCER DANS WINDOWS, dans PowerShell EN ADMINISTRATEUR, depuis le dossier Outils :
        powershell -ExecutionPolicy Bypass -File installer-ssd.ps1
        powershell -ExecutionPolicy Bypass -File installer-ssd.ps1 -Iso C:\chemin\FileSystemOS.iso
    Deja installe ? Le relancer ne reinstalle rien : il compare le noyau de l'ISO a celui du
    SSD et ne remplace que le noyau s'il a change (pas de telechargement, pas de GRUB).
    Pour forcer une installation complete : ajoutez -Reinstaller.
    Pour tout annuler :
        powershell -ExecutionPolicy Bypass -File installer-ssd.ps1 -Retirer

    Ensuite, au BIOS : F2 > General > Boot Sequence > Boot List Option : Legacy,
    puis demarrez sur "Internal HDD". Pour revenir a Windows : Boot List Option : UEFI.
#>
param(
    [string]$Iso = "",
    [switch]$Retirer,
    [switch]$Reinstaller
)

$ErrorActionPreference = "Stop"
$TypeBiosBoot = "21686148-6449-6e6f-744e-656564454649"
$UrlGrub = "https://ftp.gnu.org/gnu/grub/grub-2.06-for-windows.zip"
$Marque = "INSTALLE-PAR-FILESYSTEMOS.txt"
$Docs = [Environment]::GetFolderPath("MyDocuments")
$FichierEtat = Join-Path $Docs "FSOS-installation-ssd.txt"

function Etape($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)    { Write-Host "   $t" -ForegroundColor Green }
function Info($t)  { Write-Host "   $t" }
function Stop-Erreur($t) { Write-Host ""; Write-Host "ERREUR : $t" -ForegroundColor Red; exit 1 }
function Lire-Etat {
    $e = @{}
    if (Test-Path $FichierEtat) {
        foreach ($ligne in Get-Content $FichierEtat) { $k, $v = $ligne -split "=", 2; if ($v) { $e[$k.Trim()] = $v.Trim() } }
    }
    return $e
}

# Acces brut au disque (le .NET de Windows PowerShell refuse les chemins \\.\ directement)
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class FsosDisque
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string nom, uint acces, uint partage, IntPtr securite,
                                            uint creation, uint drapeaux, IntPtr modele);

    static FileStream Ouvrir(int disque, bool ecriture)
    {
        uint acces = ecriture ? 0xC0000000u : 0x80000000u;
        SafeFileHandle h = CreateFile(@"\\.\PhysicalDrive" + disque, acces, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("ouverture du disque impossible (erreur " + Marshal.GetLastWin32Error() + ")");
        return new FileStream(h, ecriture ? FileAccess.ReadWrite : FileAccess.Read, 1);
    }

    public static byte[] Lire(int disque, long position, int octets)
    {
        using (FileStream fs = Ouvrir(disque, false))
        {
            byte[] b = new byte[octets];
            fs.Position = position;
            int lu = 0;
            while (lu < octets) { int n = fs.Read(b, lu, octets - lu); if (n <= 0) break; lu += n; }
            if (lu != octets) throw new IOException("lecture incomplete du disque");
            return b;
        }
    }

    public static void EcrireSecteur0(int disque, byte[] secteur)
    {
        if (secteur.Length != 512) throw new ArgumentException("un secteur fait 512 octets");
        using (FileStream fs = Ouvrir(disque, true)) { fs.Position = 0; fs.Write(secteur, 0, 512); fs.Flush(); }
    }
}
"@

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
          [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Stop-Erreur "lancez PowerShell EN ADMINISTRATEUR (clic droit > Executer en tant qu'administrateur)." }

# ---------------------------------------------------------------------------
# Mode -Retirer : tout remettre comme avant
# ---------------------------------------------------------------------------
if ($Retirer) {
    Etape "Retrait du demarrage de File System OS sur le SSD"
    $etat = Lire-Etat
    if (-not $etat["disque"]) { Stop-Erreur "rien a retirer ($FichierEtat introuvable)." }
    $num = [int]$etat["disque"]
    $sauvegarde = $etat["sauvegarde"]
    if (-not (Test-Path $sauvegarde)) { Stop-Erreur "sauvegarde introuvable : $sauvegarde" }

    # Programme de demarrage d'origine (octets 0 a 439), table de partitions actuelle conservee
    $origine = [System.IO.File]::ReadAllBytes($sauvegarde)
    $actuel = [FsosDisque]::Lire($num, 0, 512)
    [Array]::Copy($origine, 0, $actuel, 0, 440)
    [FsosDisque]::EcrireSecteur0($num, $actuel)
    Ok "debut du disque $num remis comme a l'origine"

    if ($etat["biosboot"]) {
        $p = Get-Partition -DiskNumber $num | Where-Object { $_.Offset -eq [long]$etat["biosboot"] }
        if ($p) { Remove-Partition -DiskNumber $num -PartitionNumber $p.PartitionNumber -Confirm:$false; Ok "petite partition de demarrage BIOS supprimee" }
    }
    $vol = Get-Volume -FileSystemLabel "FSOS" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($vol -and $vol.DriveLetter -and (Test-Path "$($vol.DriveLetter):\boot\grub\$Marque")) {
        Remove-Item "$($vol.DriveLetter):\boot" -Recurse -Force
        Ok "GRUB et noyau retires de la partition FSOS (ta session FSOS est intacte)"
    }
    Remove-Item $FichierEtat -Force
    Ok "Termine. Windows n'a jamais ete touche."
    exit 0
}

# ---------------------------------------------------------------------------
# 1. Partition FSOS et disque
# ---------------------------------------------------------------------------
Etape "1/7  Partition FSOS"
$vol = Get-Volume -FileSystemLabel "FSOS" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $vol) { Stop-Erreur "aucune partition nommee FSOS (Gestion des disques : FAT32, nom FSOS)." }
if ($vol.FileSystem -ne "FAT32") { Stop-Erreur "la partition FSOS est en $($vol.FileSystem) : elle doit etre en FAT32." }
$L = $vol.DriveLetter
if (-not $L) { Stop-Erreur "la partition FSOS n'a pas de lettre de lecteur." }
$num = (Get-Partition -DriveLetter $L).DiskNumber
$disque = Get-Disk -Number $num
Ok "FSOS = $($L): sur le disque $num : $($disque.FriendlyName)"
if ($disque.BusType -eq "USB") { Stop-Erreur "la partition FSOS est sur un disque USB : ce script sert au SSD interne." }
if ($disque.PartitionStyle -ne "GPT") { Stop-Erreur "le disque $num n'est pas en GPT : cas non prevu par ce script." }
if ($disque.LogicalSectorSize -ne 512) { Stop-Erreur "secteurs de $($disque.LogicalSectorSize) octets : cas non prevu." }
$debut = [FsosDisque]::Lire($num, 0, 1MB)
if ($debut[510] -ne 0x55 -or $debut[511] -ne 0xAA -or $debut[450] -ne 0xEE) {
    Stop-Erreur "le premier secteur du disque n'a pas la forme attendue (MBR de protection GPT)."
}
Ok "disque GPT verifie"

# ---------------------------------------------------------------------------
# 2. Sauvegarde du debut du disque (la toute premiere est gardee pour toujours)
# ---------------------------------------------------------------------------
Etape "2/7  Sauvegarde du debut du disque"
$etat = Lire-Etat
if ($etat["sauvegarde"] -and (Test-Path $etat["sauvegarde"])) {
    $sauvegarde = $etat["sauvegarde"]
    Ok "sauvegarde d'origine deja faite : $sauvegarde"
} else {
    $sauvegarde = Join-Path $Docs ("FSOS-sauvegarde-disque$num-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bin")
    [System.IO.File]::WriteAllBytes($sauvegarde, $debut)
    Ok "premier Mo du disque sauvegarde : $sauvegarde"
}
$biosboot = $etat["biosboot"]
@("disque=$num", "sauvegarde=$sauvegarde", "biosboot=$biosboot") | Set-Content $FichierEtat

# ---------------------------------------------------------------------------
# 3. Noyau et menu de demarrage, pris dans l'ISO (exactement comme sur la cle)
# ---------------------------------------------------------------------------
Etape "3/7  Noyau de File System OS"
if ($Iso -eq "") {
    $racine = Split-Path $PSScriptRoot -Parent
    $trouvee = Get-ChildItem -Path $racine -Recurse -Filter *.iso -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($trouvee -eq $null) { Stop-Erreur "aucune ISO trouvee. Compilez le projet, ou indiquez -Iso C:\...\FileSystemOS.iso" }
    $Iso = $trouvee.FullName
}
if (-not (Test-Path $Iso)) { Stop-Erreur "ISO introuvable : $Iso" }
Info "ISO : $Iso ($((Get-Item $Iso).LastWriteTime))"

$tmp = Join-Path $env:TEMP "fsos-installation-ssd"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $tmp | Out-Null
New-Item -ItemType Directory "$tmp\iso" | Out-Null

$IsoPlein = (Resolve-Path $Iso).Path
$img = Mount-DiskImage -ImagePath $IsoPlein -PassThru
$cfg = $null
$noyaux = @()
try {
    Start-Sleep -Seconds 2
    $li = ($img | Get-Volume).DriveLetter
    if (-not $li) { Stop-Erreur "l'ISO n'a pas recu de lettre de lecteur." }

    $cheminCfg = "$($li):\boot\grub\grub.cfg"
    if (Test-Path $cheminCfg) {
        $cfg = Get-Content $cheminCfg -Raw
        # Lignes qui cherchent le CD ou la cle : inutiles sur le SSD (GRUB y connait deja sa partition)
        $cfg = [regex]::Replace($cfg, "(?m)^(\s*)(set\s+root=.*|search\s.*)$", '$1# (retire par installer-ssd.ps1) $2')
        # Les fichiers que le menu charge (lignes multiboot / multiboot2)
        foreach ($m in [regex]::Matches($cfg, "(?m)^\s*multiboot2?\s+(\S+)")) {
            $p = $m.Groups[1].Value -replace "^\([^)]*\)", ""
            $source = "$($li):" + ($p -replace "/", "\")
            if (Test-Path $source) {
                $dest = "$tmp\iso" + ($p -replace "/", "\")
                New-Item -ItemType Directory (Split-Path $dest -Parent) -Force | Out-Null
                Copy-Item $source $dest -Force
                $noyaux += $p
            }
        }
    }
    if ($noyaux.Count -eq 0) {
        # Pas de menu GRUB lisible : le plus gros .bin de l'ISO, menu ecrit par le script
        $n = Get-ChildItem "$($li):\" -Recurse -Include *.bin,*.elf -ErrorAction SilentlyContinue |
             Where-Object { $_.Length -gt 100KB } | Sort-Object Length | Select-Object -Last 1
        if ($n -eq $null) { Stop-Erreur "noyau introuvable dans l'ISO." }
        New-Item -ItemType Directory "$tmp\iso\boot" -Force | Out-Null
        Copy-Item $n.FullName "$tmp\iso\boot\FileSystemOS.bin"
        $octets = [System.IO.File]::ReadAllBytes("$tmp\iso\boot\FileSystemOS.bin")
        $proto = "multiboot"
        for ($i = 0; $i -lt [math]::Min(32768, $octets.Length - 4); $i += 4) {
            $v = [BitConverter]::ToUInt32($octets, $i)
            if ($v -eq 0xE85250D6 -and ($i % 8) -eq 0) { $proto = "multiboot2"; break }
            if ($v -eq 0x1BADB002) { break }
        }
        $cfg = "set timeout=3`nmenuentry `"File System OS`" {`n    $proto /boot/FileSystemOS.bin`n    boot`n}`n"
        $noyaux = @("/boot/FileSystemOS.bin")
        Info "menu de l'ISO introuvable : menu de secours ($proto)"
    }
} finally { Dismount-DiskImage -ImagePath $IsoPlein | Out-Null }
foreach ($k in $noyaux) {
    $t = (Get-Item ("$tmp\iso" + ($k -replace "/", "\"))).Length
    Ok "noyau : $k ($([math]::Round($t / 1MB, 1)) Mo)"
}

# Version du noyau : File System OS y range le marqueur "FSOS-NOYAU-VERSION=x.y;"
$version = "inconnue"
$octetsNoyau = [System.IO.File]::ReadAllBytes("$tmp\iso" + ($noyaux[0] -replace "/", "\"))
$lectures = @(
    [Text.Encoding]::Unicode.GetString($octetsNoyau, 0, $octetsNoyau.Length - 1),
    [Text.Encoding]::Unicode.GetString($octetsNoyau, 1, $octetsNoyau.Length - 1),
    [Text.Encoding]::ASCII.GetString($octetsNoyau))
foreach ($texteNoyau in $lectures) {
    $m = [regex]::Match($texteNoyau, "FSOS-NOYAU-VERSION=([0-9A-Za-z\.\-]+);")
    if ($m.Success) { $version = $m.Groups[1].Value; break }
}
Ok "version du noyau de l'ISO : $version"

# ---------------------------------------------------------------------------
# Deja installe : on ne reinstalle pas, on met seulement le noyau a jour s'il a change
# ---------------------------------------------------------------------------
$s0 = [FsosDisque]::Lire($num, 0, 512)
$dejaGrub = ([Text.Encoding]::ASCII.GetString($s0, 0, 440)).Contains("GRUB") -and (Test-Path "$($L):\boot\grub\$Marque")
if ($dejaGrub -and -not $Reinstaller) {
    Etape "Deja installe : mise a jour du noyau seulement"
    $avant = if (Test-Path "$($L):\boot\noyau-version.txt") { (Get-Content "$($L):\boot\noyau-version.txt" -Raw).Trim() } else { "inconnue" }
    $change = $false
    foreach ($k in $noyaux) {
        $src = "$tmp\iso" + ($k -replace "/", "\")
        $dst = "$($L):" + ($k -replace "/", "\")
        if ((Test-Path $dst) -and ((Get-FileHash $src).Hash -eq (Get-FileHash $dst).Hash)) { continue }
        New-Item -ItemType Directory (Split-Path $dst -Parent) -Force | Out-Null
        Copy-Item $src $dst -Force
        $change = $true
    }
    Set-Content -Path "$($L):\boot\grub\grub.cfg" -Value $cfg -Encoding ASCII
    Set-Content -Path "$($L):\boot\noyau-version.txt" -Value $version -Encoding ASCII
    Write-Host ""
    if ($change) { Write-Host "TERMINE : noyau du SSD mis a jour ($avant -> $version)." -ForegroundColor Green }
    else { Write-Host "TERMINE : le SSD est deja a jour (version $version), rien a reinstaller." -ForegroundColor Green }
    exit 0
}

# ---------------------------------------------------------------------------
# 4. GRUB pour Windows (projet GNU, version 2.06 comme Rufus)
# ---------------------------------------------------------------------------
Etape "4/7  GRUB pour Windows"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try { Invoke-WebRequest -Uri $UrlGrub -OutFile "$tmp\grub.zip" -UseBasicParsing }
catch {
    Stop-Erreur "telechargement de GRUB impossible (Internet ?).`n         Telechargez $UrlGrub`n         dans $tmp\grub.zip, puis relancez."
}
Expand-Archive "$tmp\grub.zip" "$tmp\grub" -Force
$grubInstall = Get-ChildItem "$tmp\grub" -Recurse -Filter grub-install.exe | Select-Object -First 1
if ($grubInstall -eq $null) { Stop-Erreur "grub-install.exe introuvable dans l'archive de GRUB." }
Ok "GRUB 2.06 pret : $($grubInstall.FullName)"

# ---------------------------------------------------------------------------
# 5. Petite partition de demarrage BIOS (1 Mo, ou GRUB range la suite de son programme)
# ---------------------------------------------------------------------------
Etape "5/7  Partition de demarrage BIOS"
$force = $false
$pbb = Get-Partition -DiskNumber $num | Where-Object { $_.GptType -eq "{$TypeBiosBoot}" } | Select-Object -First 1
if ($pbb) {
    Ok "deja presente (decalage $($pbb.Offset))"
} else {
    # A la FIN d'un espace libre : C: pourra toujours s'etendre dans le reste
    $parts = Get-Partition -DiskNumber $num | Sort-Object Offset
    $offset = $null
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $fin = $parts[$i].Offset + $parts[$i].Size
        $suivant = if ($i + 1 -lt $parts.Count) { $parts[$i + 1].Offset } else { $disque.Size - 2MB }
        if ($suivant - $fin -ge 4MB) { $offset = [long]([math]::Floor(($suivant - 2MB) / 1MB) * 1MB); break }
    }
    if ($offset -eq $null) {
        Info "pas d'espace libre de 4 Mo : GRUB utilisera une methode de secours (fichier sur FSOS)."
        $force = $true
    } else {
        $script = "select disk $num`r`ncreate partition primary size=1 offset=$($offset / 1KB) id=$TypeBiosBoot`r`n"
        Set-Content "$tmp\diskpart.txt" $script -Encoding ASCII
        diskpart /s "$tmp\diskpart.txt" | Out-Null
        $pbb = Get-Partition -DiskNumber $num | Where-Object { $_.GptType -eq "{$TypeBiosBoot}" } | Select-Object -First 1
        if ($pbb) {
            $biosboot = $pbb.Offset
            Ok "creee : 1 Mo au decalage $biosboot (invisible pour Windows)"
        } else {
            Info "creation impossible : GRUB utilisera une methode de secours (fichier sur FSOS)."
            $force = $true
        }
    }
}
@("disque=$num", "sauvegarde=$sauvegarde", "biosboot=$biosboot") | Set-Content $FichierEtat

# ---------------------------------------------------------------------------
# 6. Installation : fichiers sur FSOS, puis GRUB au debut du SSD
# ---------------------------------------------------------------------------
Etape "6/7  Installation"
Copy-Item "$tmp\iso\*" "$($L):\" -Recurse -Force
Ok "noyau copie sur $($L):"

Write-Host ""
Write-Host "   Derniere etape : ecriture de GRUB au debut du disque $num." -ForegroundColor Yellow
Write-Host "   (sauvegarde d'origine : $sauvegarde)" -ForegroundColor Yellow
$rep = ""
while ($rep -ne "o" -and $rep -ne "n") { $rep = (Read-Host "   Continuer ? Tapez o puis Entree (ou n pour arreter)").Trim().ToLower() }
if ($rep -ne "o") { Info "Arrete. Rien n'a ete ecrit au debut du disque."; exit 0 }

$cible = "//./PHYSICALDRIVE$num"
$options = @("--target=i386-pc", "--no-floppy", "--boot-directory=$($L):\boot")
if ($force) { $options += "--force" }
Push-Location $grubInstall.DirectoryName
try {
    & $grubInstall.FullName @options $cible
    if ($LASTEXITCODE -ne 0 -and -not $force) {
        Info "nouvel essai avec la methode de secours..."
        & $grubInstall.FullName @options "--force" $cible
    }
} finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { Stop-Erreur "GRUB a echoue (code $LASTEXITCODE). Pour tout annuler : installer-ssd.ps1 -Retirer" }

# Le menu de demarrage APRES grub-install (qui ne doit pas l'ecraser), et la version du noyau
Set-Content -Path "$($L):\boot\grub\grub.cfg" -Value $cfg -Encoding ASCII
Set-Content -Path "$($L):\boot\noyau-version.txt" -Value $version -Encoding ASCII
"Installe par installer-ssd.ps1 (File System OS). Retrait : installer-ssd.ps1 -Retirer" |
    Set-Content "$($L):\boot\grub\$Marque"
Ok "GRUB installe, menu de demarrage copie depuis l'ISO"

# ---------------------------------------------------------------------------
# 7. Verification
# ---------------------------------------------------------------------------
Etape "7/7  Verification"
$s0 = [FsosDisque]::Lire($num, 0, 512)
$texte = [Text.Encoding]::ASCII.GetString($s0, 0, 440)
if ($texte.Contains("GRUB")) { Ok "GRUB est bien au debut du SSD" }
else { Stop-Erreur "GRUB n'apparait pas au debut du disque : l'installation n'a pas abouti." }
foreach ($k in $noyaux) {
    if (Test-Path ("$($L):" + ($k -replace "/", "\"))) { Ok "noyau present : $($L):$($k -replace '/', '\')" }
}

Write-Host ""
Write-Host "TERMINE." -ForegroundColor Green
Write-Host "   Pour demarrer File System OS depuis le SSD, sans la cle :"
Write-Host "     F2 > General > Boot Sequence > Boot List Option : Legacy"
Write-Host "     puis demarrez sur 'Internal HDD' (ou choisissez-le avec F12)."
Write-Host "   Si rien ne demarre : depuis la cle, Console > ssd amorce sata0 actif"
Write-Host "   Pour revenir a Windows : Boot List Option : UEFI."
Write-Host "   Pour tout annuler : installer-ssd.ps1 -Retirer"
