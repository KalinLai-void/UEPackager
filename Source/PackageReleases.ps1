param([string]$PublishedDirectory = (Join-Path $PSScriptRoot '../Releases'), [string]$ArchiveDirectory = (Join-Path $PSScriptRoot '../Packages'))
# PowerShell 7.4+ (.NET 8) is needed for System.Formats.Tar.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Formats.Tar
New-Item -ItemType Directory -Force $ArchiveDirectory | Out-Null
$iconBytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'Assets/App.ico'))
$count=[BitConverter]::ToUInt16($iconBytes,4)
$entry=6+16*($count-1)
$length=[BitConverter]::ToUInt32($iconBytes,$entry+8)
$offset=[BitConverter]::ToUInt32($iconBytes,$entry+12)
$png=New-Object byte[] $length
[Array]::Copy($iconBytes,$offset,$png,0,$length)
function Write-BigEndian([IO.Stream]$Stream,[int]$Value) { $bytes=[BitConverter]::GetBytes($Value); [Array]::Reverse($bytes);$Stream.Write($bytes,0,4) }
foreach($runtime in @('win-x64','win-arm64','osx-x64','osx-arm64','linux-x64','linux-arm64')) {
    $folder=Join-Path $PublishedDirectory $runtime
    if(!(Test-Path -LiteralPath $folder)){throw "Missing publish directory: $folder"}
    $work=Join-Path ([IO.Path]::GetTempPath()) ('ue-release-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $work | Out-Null
    $name='UEPackager-'+$runtime
    $payload=Join-Path $work $name
    New-Item -ItemType Directory $payload | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $payload 'README.md')
    if($runtime.StartsWith('osx-')) {
        $contents=Join-Path $payload 'UEPackager.app/Contents'
        $binaries=Join-Path $contents 'MacOS';$resources=Join-Path $contents 'Resources'
        New-Item -ItemType Directory -Force $binaries,$resources | Out-Null
        Get-ChildItem -LiteralPath $folder -File | Where-Object Name -ne 'Settings.json' | Copy-Item -Destination $binaries
        $plist=@'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>UEPackager</string>
<key>CFBundleDisplayName</key><string>UEPackager</string>
<key>CFBundleIdentifier</key><string>tools.uepackaging.automation</string>
<key>CFBundleExecutable</key><string>UEPackager</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleIconFile</key><string>App.icns</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>12.0</string>
</dict></plist>
'@
        [IO.File]::WriteAllText((Join-Path $contents 'Info.plist'),$plist)
        $stream=[IO.File]::Create((Join-Path $resources 'App.icns'))
        try {$bytes=[Text.Encoding]::ASCII.GetBytes('icns');$stream.Write($bytes);Write-BigEndian $stream ($png.Length+16);$bytes=[Text.Encoding]::ASCII.GetBytes('ic08');$stream.Write($bytes);Write-BigEndian $stream ($png.Length+8);$stream.Write($png)} finally {$stream.Dispose()}
    } else {
        Get-ChildItem -LiteralPath $folder -File | Where-Object Name -ne 'Settings.json' | Copy-Item -Destination $payload
        if($runtime.StartsWith('linux-')) {
            [IO.File]::WriteAllBytes((Join-Path $payload 'App.png'),$png)
            $launch=@'
#!/usr/bin/env sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec ./UEPackager "$@"
'@
            [IO.File]::WriteAllText((Join-Path $payload 'Launch.sh'),($launch.Replace("`r`n","`n")+"`n"))
        }
    }
    if($runtime.StartsWith('win-')) {
        $archive=Join-Path $ArchiveDirectory ($name+'.zip')
        if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}
        [IO.Compression.ZipFile]::CreateFromDirectory($payload,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
    } else {
        $archive=Join-Path $ArchiveDirectory ($name+'.tar.gz')
        $file=[IO.File]::Create($archive)
        $gzip=[IO.Compression.GZipStream]::new($file,[IO.Compression.CompressionLevel]::Optimal,$false)
        $tar=[System.Formats.Tar.TarWriter]::new($gzip,$true)
        try {
            foreach($item in Get-ChildItem -LiteralPath $payload -Recurse -File) {
                $relative=[IO.Path]::GetRelativePath($work,$item.FullName).Replace('\','/')
                $record=[System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile,$relative)
                $record.Mode=[IO.UnixFileMode]420 # 0644
                if($item.Name -eq 'UEPackager' -or $item.Extension -in @('.sh','.dylib','.so')){$record.Mode=[IO.UnixFileMode]493} # 0755
                $record.Uid=0;$record.Gid=0
                $data=[IO.File]::OpenRead($item.FullName)
                try {$record.DataStream=$data;$tar.WriteEntry($record)} finally {$data.Dispose()}
            }
        } finally {$tar.Dispose();$gzip.Dispose();$file.Dispose()}
    }
    # Only remove this invocation's explicitly created temporary work directory.
    $resolved=[IO.Path]::GetFullPath($work);$temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith('ue-release-')){Remove-Item -LiteralPath $resolved -Recurse -Force}
    Write-Output $archive
}
$hashes=Get-ChildItem -LiteralPath $ArchiveDirectory -File | Where-Object Extension -in @('.gz','.zip') | Get-FileHash -Algorithm SHA256
$hashes | ForEach-Object {$_.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_.Path)} | Set-Content -LiteralPath (Join-Path $ArchiveDirectory 'SHA256SUMS.txt') -Encoding utf8
