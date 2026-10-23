<#
    demarrage-ssd.ps1  -  File System OS Company (Vincent Senut)

    DEMARRER FILE SYSTEM OS DEPUIS LE SSD, EN MODE LEGACY, SANS LA CLE USB.

    Comment ca marche :
      - en mode Legacy, le BIOS execute le petit programme place dans le tout premier secteur
        du disque. Sur un disque Windows UEFI, ce programme est absent : Windows n'en a pas besoin ;
      - ce script y place le chargeur Limine, le meme que sur la cle ;
      - Limine range la suite de son programme dans une toute petite partition a lui (32 Mo),
        creee dans l'espace non alloue : la table de partitions n'est jamais modifiee par Limine ;
      - le noyau de File System OS et le menu sont copies sur la partition FSOS.

    Windows n'est PAS modifie : il demarre en UEFI, et l'UEFI ignore ce premier secteur.
    Mais en mode Legacy, seul File System OS peut demarrer. Pour revenir a Windows, remettez
    le BIOS en UEFI : F2 > General > Boot Sequence > Boot List Option.

    A LANCER DANS WINDOWS, dans PowerShell EN ADMINISTRATEUR :
        powershell -ExecutionPolicy Bypass -File demarrage-ssd.ps1
        powershell -ExecutionPolicy Bypass -File demarrage-ssd.ps1 -Iso C:\chemin\FileSystemOS.iso
    Apres une recompilation, relancez la meme commande : le noyau est mis a jour.
    Pour tout retirer :
        powershell -ExecutionPolicy Bypass -File demarrage-ssd.ps1 -Retirer

    Securite :
      - le premier Mo du disque est sauvegarde dans Documents avant toute ecriture ;
      - le script s'arrete si la partition FSOS est absente, n'est pas en FAT32,
        ou si le disque n'a pas la structure attendue ;
      - -Retirer remet le premier secteur d'origine et supprime la petite partition.
#>
param(
    [string]$Iso = "",
    [switch]$Retirer
)

$ErrorActionPreference = "Stop"
$TaillePetite = 32MB
$Marque = "INSTALLE-PAR-FILESYSTEMOS.txt"
$Docs = [Environment]::GetFolderPath("MyDocuments")
$FichierEtat = Join-Path $Docs "FSOS-demarrage-ssd.txt"

function Etape($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)    { Write-Host "   $t" -ForegroundColor Green }
function Info($t)  { Write-Host "   $t" }
function Stop-Erreur($t) { Write-Host ""; Write-Host "ERREUR : $t" -ForegroundColor Red; exit 1 }

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
        uint acces = ecriture ? 0xC0000000u : 0x80000000u;          // lecture (+ ecriture)
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

# ---------------------------------------------------------------------------
# 0. Verifications communes
# ---------------------------------------------------------------------------
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
          [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Stop-Erreur "lancez PowerShell EN ADMINISTRATEUR (clic droit > Executer en tant qu'administrateur)." }

# ---------------------------------------------------------------------------
# Mode -Retirer
# ---------------------------------------------------------------------------
if ($Retirer) {
    Etape "Retrait du demarrage de File System OS sur le SSD"
    if (-not (Test-Path $FichierEtat)) { Stop-Erreur "rien a retirer ($FichierEtat introuvable)." }
    $etat = @{}
    foreach ($ligne in Get-Content $FichierEtat) { $k, $v = $ligne -split "=", 2; if ($v) { $etat[$k.Trim()] = $v.Trim() } }
    $num = [int]$etat["disque"]; $offset = [long]$etat["decalage"]; $sauvegarde = $etat["sauvegarde"]
    if (-not (Test-Path $sauvegarde)) { Stop-Erreur "sauvegarde introuvable : $sauvegarde" }

    # Premier secteur : on remet le programme d'origine (octets 0 a 439) et on garde la table actuelle
    $origine = [System.IO.File]::ReadAllBytes($sauvegarde)
    $actuel = [FsosDisque]::Lire($num, 0, 512)
    [Array]::Copy($origine, 0, $actuel, 0, 440)
    [FsosDisque]::EcrireSecteur0($num, $actuel)
    Ok "premier secteur du disque $num remis comme a l'origine"

    $petite = Get-Partition -DiskNumber $num | Where-Object { $_.Offset -eq $offset }
    if ($petite) { Remove-Partition -DiskNumber $num -PartitionNumber $petite.PartitionNumber -Confirm:$false; Ok "petite partition de Limine supprimee" }

    $vol = Get-Volume -FileSystemLabel "FSOS" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($vol -and $vol.DriveLetter -and (Test-Path "$($vol.DriveLetter):\boot\limine\$Marque")) {
        Remove-Item "$($vol.DriveLetter):\boot\limine" -Recurse -Force
        Remove-Item "$($vol.DriveLetter):\boot\FileSystemOS.bin" -Force -ErrorAction SilentlyContinue
        Ok "chargeur et noyau retires de la partition FSOS (tes fichiers sont intacts)"
    }
    Remove-Item $FichierEtat -Force
    Ok "Termine. En Legacy, le SSD ne demarre plus rien ; Windows (UEFI) n'a jamais ete touche."
    exit 0
}

# ---------------------------------------------------------------------------
# 1. Partition FSOS et disque
# ---------------------------------------------------------------------------
Etape "1/6  Partition FSOS"
$vol = Get-Volume -FileSystemLabel "FSOS" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $vol) { Stop-Erreur "aucune partition nommee FSOS. Creez-la dans la Gestion des disques (FAT32, nom FSOS)." }
if ($vol.FileSystem -ne "FAT32") { Stop-Erreur "la partition FSOS est en $($vol.FileSystem) : elle doit etre en FAT32." }
$L = $vol.DriveLetter
if (-not $L) { Stop-Erreur "la partition FSOS n'a pas de lettre de lecteur : donnez-lui en une dans la Gestion des disques." }
$partFsos = Get-Partition -DriveLetter $L
$num = $partFsos.DiskNumber
$disque = Get-Disk -Number $num
Ok "FSOS = $($L): ($([math]::Round($vol.Size / 1GB, 2)) Go) sur le disque $num : $($disque.FriendlyName)"
if ($disque.BusType -eq "USB") { Stop-Erreur "la partition FSOS est sur un disque USB : ce script sert au disque interne." }
if ($disque.PartitionStyle -ne "GPT") { Stop-Erreur "le disque $num n'est pas en GPT ($($disque.PartitionStyle)) : cas non prevu par ce script." }
if ($disque.LogicalSectorSize -ne 512) { Stop-Erreur "secteurs de $($disque.LogicalSectorSize) octets : cas non prevu." }

# Le premier Mo du disque, tel qu'il est maintenant
$debut = [FsosDisque]::Lire($num, 0, 1MB)
if ($debut[510] -ne 0x55 -or $debut[511] -ne 0xAA -or $debut[450] -ne 0xEE) {
    Stop-Erreur "le premier secteur du disque n'a pas la forme attendue (MBR de protection GPT)."
}
$sig = [Text.Encoding]::ASCII.GetString($debut, 512, 8)
if ($sig -ne "EFI PART") { Stop-Erreur "en-tete GPT introuvable." }
Ok "disque GPT verifie"

# ---------------------------------------------------------------------------
# 2. Sauvegarde du premier Mo (une seule fois : on garde toujours l'original)
# ---------------------------------------------------------------------------
Etape "2/6  Sauvegarde"
$etat = @{}
if (Test-Path $FichierEtat) {
    foreach ($ligne in Get-Content $FichierEtat) { $k, $v = $ligne -split "=", 2; if ($v) { $etat[$k.Trim()] = $v.Trim() } }
}
if ($etat["sauvegarde"] -and (Test-Path $etat["sauvegarde"])) {
    $sauvegarde = $etat["sauvegarde"]
    Ok "sauvegarde d'origine deja faite : $sauvegarde"
} else {
    $sauvegarde = Join-Path $Docs ("FSOS-sauvegarde-disque$num-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bin")
    [System.IO.File]::WriteAllBytes($sauvegarde, $debut)
    Ok "premier Mo du disque sauvegarde : $sauvegarde"
}

# ---------------------------------------------------------------------------
# 3. Noyau de File System OS (depuis l'ISO)
# ---------------------------------------------------------------------------
Etape "3/6  Noyau de File System OS"
if ($Iso -eq "") {
    $racine = Split-Path $PSScriptRoot -Parent
    $trouvee = Get-ChildItem -Path $racine -Recurse -Filter *.iso -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($trouvee -eq $null) { Stop-Erreur "aucune ISO trouvee. Compilez le projet, ou indiquez -Iso C:\...\FileSystemOS.iso" }
    $Iso = $trouvee.FullName
}
if (-not (Test-Path $Iso)) { Stop-Erreur "ISO introuvable : $Iso" }
Info "ISO : $Iso"

$tmp = Join-Path $env:TEMP "fsos-ssd"
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

$octets = [System.IO.File]::ReadAllBytes("$tmp\FileSystemOS.bin")
$proto = "multiboot1"
for ($i = 0; $i -lt [math]::Min(32768, $octets.Length - 4); $i += 4) {
    $v = [BitConverter]::ToUInt32($octets, $i)
    if ($v -eq 0xE85250D6 -and ($i % 8) -eq 0) { $proto = "multiboot2"; break }
    if ($v -eq 0x1BADB002) { $proto = "multiboot1"; break }
}
Ok "protocole : $proto"

# ---------------------------------------------------------------------------
# 4. Chargeur Limine (version BIOS / Legacy)
# ---------------------------------------------------------------------------
Etape "4/6  Chargeur Limine"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$base = "https://github.com/limine-bootloader/limine/raw/v8.x-binary"
try {
    Invoke-WebRequest -Uri "$base/limine.exe" -OutFile "$tmp\limine.exe" -UseBasicParsing
    Invoke-WebRequest -Uri "$base/limine-bios.sys" -OutFile "$tmp\limine-bios.sys" -UseBasicParsing
} catch {
    Stop-Erreur "telechargement de Limine impossible (Internet ?).`n         Telechargez limine.exe et limine-bios.sys depuis`n         $base`n         et placez-les dans $tmp, puis relancez."
}
Ok "limine.exe et limine-bios.sys telecharges"

# ---------------------------------------------------------------------------
# 5. Petite partition de Limine (32 Mo, sans systeme de fichiers)
# ---------------------------------------------------------------------------
Etape "5/6  Petite partition de Limine"
$petite = $null
if ($etat["decalage"]) { $petite = Get-Partition -DiskNumber $num | Where-Object { $_.Offset -eq [long]$etat["decalage"] } }
if ($petite) {
    Ok "deja creee (au decalage $($petite.Offset))"
} else {
    # On la place a la FIN d'un espace libre : C: pourra toujours s'etendre dans le reste
    $parts = Get-Partition -DiskNumber $num | Sort-Object Offset
    $offset = $null
    for ($i = 0; $i -lt $parts.Count; $i++) {
        $fin = $parts[$i].Offset + $parts[$i].Size
        $suivant = if ($i + 1 -lt $parts.Count) { $parts[$i + 1].Offset } else { $disque.Size - 1MB }
        if ($suivant - $fin -ge $TaillePetite + 1MB) {
            $offset = [long]([math]::Floor(($suivant - $TaillePetite) / 1MB) * 1MB)
            break
        }
    }
    if ($offset -eq $null) {
        Stop-Erreur "aucun espace non alloue de 33 Mo sur le disque $num.`n         Dans la Gestion des disques : clic droit sur C: > Reduire le volume > 64 Mo, puis relancez."
    }
    $petite = New-Partition -DiskNumber $num -Offset $offset -Size $TaillePetite
    Set-Partition -DiskNumber $num -PartitionNumber $petite.PartitionNumber -NoDefaultDriveLetter $true
    Ok "creee : $([math]::Round($TaillePetite / 1MB)) Mo au decalage $offset (aucune lettre, aucun formatage)"
}

# Numero de la petite partition DANS LA TABLE GPT (c'est celui que Limine attend)
$gpt = [FsosDisque]::Lire($num, 0, 1MB)
$lbaEntrees = [BitConverter]::ToUInt64($gpt, 512 + 72)
$nbEntrees = [BitConverter]::ToUInt32($gpt, 512 + 80)
$tailleEntree = [BitConverter]::ToUInt32($gpt, 512 + 84)
$entrees = [FsosDisque]::Lire($num, [long]$lbaEntrees * 512, [int][math]::Ceiling($nbEntrees * $tailleEntree / 512) * 512)
$index = 0
for ($i = 0; $i -lt $nbEntrees; $i++) {
    $p = $i * $tailleEntree
    $premierLba = [BitConverter]::ToUInt64($entrees, $p + 32)
    if ($premierLba * 512 -eq $petite.Offset) { $index = $i + 1; break }
}
if ($index -eq 0) { Stop-Erreur "petite partition introuvable dans la table GPT : rien n'a ete ecrit au debut du disque." }
Ok "numero dans la table GPT : $index"

@("disque=$num", "decalage=$($petite.Offset)", "index=$index", "sauvegarde=$sauvegarde") | Set-Content $FichierEtat

# ---------------------------------------------------------------------------
# 6. Fichiers sur la partition FSOS, puis installation de Limine
# ---------------------------------------------------------------------------
Etape "6/6  Installation"
New-Item -ItemType Directory "$($L):\boot\limine" -Force | Out-Null
Copy-Item "$tmp\FileSystemOS.bin" "$($L):\boot\FileSystemOS.bin" -Force
Copy-Item "$tmp\limine-bios.sys" "$($L):\boot\limine\limine-bios.sys" -Force
# Plusieurs facons de lancer le noyau : si l'ecran reste noir avec la premiere,
# on essaie les suivantes depuis le menu, sans rien recompiler.
@"
timeout: 10

/File System OS
    protocol: $proto
    path: boot():/boot/FileSystemOS.bin

/File System OS - ecran 1024x768
    protocol: $proto
    path: boot():/boot/FileSystemOS.bin
    resolution: 1024x768x32

/File System OS - mode texte
    protocol: $proto
    path: boot():/boot/FileSystemOS.bin
    textmode: yes

/File System OS - multiboot1
    protocol: multiboot1
    path: boot():/boot/FileSystemOS.bin
"@ | Set-Content -Encoding ASCII "$($L):\boot\limine\limine.conf"
"Installe par demarrage-ssd.ps1 (File System OS). Supprime par : demarrage-ssd.ps1 -Retirer" |
    Set-Content "$($L):\boot\limine\$Marque"
Ok "noyau, chargeur et menu copies sur $($L):\boot"

Write-Host ""
Write-Host "   Derniere etape : ecriture de Limine au debut du disque $num." -ForegroundColor Yellow
Write-Host "   (sauvegarde d'origine : $sauvegarde)" -ForegroundColor Yellow
$rep = Read-Host "   Continuer ? (o/n)"
if ($rep -ne "o") { Info "Arrete. Rien n'a ete ecrit au debut du disque."; exit 0 }

& "$tmp\limine.exe" bios-install "\\.\PhysicalDrive$num" $index
if ($LASTEXITCODE -ne 0) {
    Stop-Erreur "Limine a echoue (code $LASTEXITCODE). Pour tout annuler : demarrage-ssd.ps1 -Retirer"
}
Ok "Limine installe sur le disque $num"

Write-Host ""
Write-Host "TERMINE." -ForegroundColor Green
Write-Host "   Pour demarrer File System OS depuis le SSD :"
Write-Host "     F2 > General > Boot Sequence > Boot List Option : Legacy"
Write-Host "     (Advanced Boot Options > Enable Legacy Option ROMs : coche)"
Write-Host "     puis choisissez 'Internal HDD' (ou F12 au logo Dell)."
Write-Host "   Pour revenir a Windows : Boot List Option : UEFI."
Write-Host "   Pour tout annuler : demarrage-ssd.ps1 -Retirer"
Write-Host ""
Write-Host "   IMPORTANT : desactivez le demarrage rapide de Windows (powercfg /h off)." -ForegroundColor Yellow
Write-Host "   Sinon Windows garde la partition FSOS en memoire pendant l'arret, et ce que" -ForegroundColor Yellow
Write-Host "   File System OS y ecrit pourrait etre ecrase au retour de Windows." -ForegroundColor Yellow
