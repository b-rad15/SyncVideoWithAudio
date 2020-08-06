InFile = %~1
OutFile = %~2
FfmpegPath = %~3
TimesArray = %~4
count = 0

rem NOT A FINISHED SCRIPT

param([System.IO.FileInfo]$InFile, [array]$TimesArray, [System.IO.FileInfo]$OutFile, [System.IO.FileInfo]$FfmpegPath)
if(!$FfmpegPath){
    $FfmpegPath = "ffmpeg.exe"
}
Remove-Item .\concat.txt
$count = 0
$files = [System.Collections.ArrayList]@()
$TimesArray | ForEach-Object {
    $fileName = "file" + ++$count + $InFile.Extension
    Write-Output "file $fileName" >> .\concat.txt
    # $ffmpegArgs = "-ss `"$_[0].ToString()`" -t `"$_[1].ToString()`" -i `"$InFile`" -c copy `"$fileName`" -y"
    # [System.Diagnostics.Process]::Start($FfmpegPath, $ffmpegArgs)
    Write-Host -NoNewline Start-Process -FilePath $FfmpegPath -ArgumentList @("-ss", [string]::Concat("`"",$_[0].ToString(), "`""), "-t", [string]::Concat("`"",$_[1].ToString(), "`""), "-i", [string]::Concat("`"",$InFile, "`""), "-c", "copy", [string]::Concat("`"",$fileName, "`""), "-y") -NoNewWindow -Wait
    Start-Process -FilePath $FfmpegPath -ArgumentList @("-ss", [string]::Concat("`"",$_[0].ToString(), "`""), "-t", [string]::Concat("`"",$_[1].ToString(), "`""), "-i", [string]::Concat("`"",$InFile, "`""), "-c", "copy", [string]::Concat("`"",$fileName, "`""), "-y") -NoNewWindow -Wait
    $files.Add($fileName)
}
# Write-Output "file section1
# file webm2.webm
# file webm3.webm" > .\concat.txt
# ffmpeg.exe -ss 00:00:00 -t 00:01:07.3113824 -i $inputwebm -c copy webm1.webm -y
# ffmpeg.exe -ss 00:01:11.4394566 -t 00:00:36.0905827 -i $inputwebm -c copy webm2.webm -y
# ffmpeg.exe -ss 00:02:03.0084951 -t 00:01:46.5321852 -i $inputwebm -c copy webm3.webm -y
# $ffmpegArgs = "-f concat -safe 0 -i concat.txt -c copy `"$OutFile`" -y"
# [System.Diagnostics.Process]::Start($FfmpegPath, $ffmpegArgs)
Write-Host Start-Process -FilePath $FfmpegPath -ArgumentList @("-f", "concat", "-safe", "0", "-i", "./concat.txt", "-c", "copy", [string]::Concat("`"",$OutFile, "`""), "-y") -NoNewWindow -Wait
Start-Process -FilePath $FfmpegPath -ArgumentList @("-f", "concat", "-safe", "0", "-i", "./concat.txt", "-c", "copy", [string]::Concat("`"",$OutFile, "`""), "-y") -NoNewWindow -Wait
$files.Add("./concat.txt")
# Start-Sleep -Seconds .5
$files | ForEach-Object {Remove-Item $_}
Write-Output "Done: $OutFile"
# Remove-Item .\concat.txt