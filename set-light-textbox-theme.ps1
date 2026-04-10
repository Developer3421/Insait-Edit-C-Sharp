param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot 'Insait Edit C Sharp'),
    [string]$NewBackground = '#FFF7F2EC',
    [string]$NewForeground = '#FF1F1A24',
    [string]$NewBorderBrush = '#FFB8A6C4',
    [string]$NewSelectionBrush = '#66E9C9B8',
    [string]$NewSelectionForeground = '#FF1F1A24',
    [string]$NewCaretBrush = '#FF6C2FA0',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project path not found: $ProjectPath"
}

function Set-SetterValue {
    param(
        [Parameter(Mandatory)] [string]$Body,
        [Parameter(Mandatory)] [string]$Property,
        [Parameter(Mandatory)] [string]$Value,
        [string]$Indent = '            '
    )

    $pattern = '(?ms)(<Setter\s+Property="' + [regex]::Escape($Property) + '"\s+Value=")[^"]*("\s*/>)'

    if ([regex]::IsMatch($Body, $pattern)) {
        return [regex]::Replace(
            $Body,
            $pattern,
            {
                param($match)
                $match.Groups[1].Value + $Value + $match.Groups[2].Value
            },
            1
        )
    }

    $trimmed = $Body.TrimEnd("`r", "`n")
    return $trimmed + "`r`n$Indent<Setter Property=`"$Property`" Value=`"$Value`"/>"
}

function Test-IsBaseTextBoxSelector {
    param([Parameter(Mandatory)] [string]$Selector)

    return $Selector -match '^TextBox(?:[.#][^":/\s]+)?$'
}

function Test-IsTemplateBorderSelector {
    param([Parameter(Mandatory)] [string]$Selector)

    return $Selector -match '^TextBox(?:[.#][^":/\s]+)?(?::(?:pointerover|focus-within))?\s*/template/\s*Border#PART_BorderElement$'
}

$stylePattern = '(?s)(?<open><Style\s+Selector="(?<selector>TextBox[^"]*)">\s*)(?<body>.*?)(?<close>\s*</Style>)'
$files = Get-ChildItem -Path $ProjectPath -Recurse -File -Filter '*.axaml' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts)\\' }

$plannedChanges = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $styleMatches = [regex]::Matches($content, $stylePattern)

    if ($styleMatches.Count -eq 0) {
        continue
    }

    $selectorsToUpdate = [System.Collections.Generic.List[string]]::new()

    foreach ($match in $styleMatches) {
        $selector = $match.Groups['selector'].Value
        $body = $match.Groups['body'].Value

        if (Test-IsTemplateBorderSelector -Selector $selector) {
            $selectorsToUpdate.Add($selector)
            continue
        }

        if (Test-IsBaseTextBoxSelector -Selector $selector) {
            $selectorsToUpdate.Add($selector)
        }
    }

    if ($selectorsToUpdate.Count -gt 0) {
        $plannedChanges.Add([pscustomobject]@{
            File      = $file.FullName
            Selectors = ($selectorsToUpdate | Select-Object -Unique)
        })
    }
}

Write-Host "Target color palette:"
Write-Host "  Background                = $NewBackground"
Write-Host "  Foreground                = $NewForeground"
Write-Host "  BorderBrush               = $NewBorderBrush"
Write-Host "  SelectionBrush            = $NewSelectionBrush"
Write-Host "  SelectionForegroundBrush  = $NewSelectionForeground"
Write-Host "  CaretBrush                = $NewCaretBrush"
Write-Host ""
Write-Host "Matched files: $($plannedChanges.Count)"

foreach ($item in $plannedChanges) {
    Write-Host "- $($item.File)"
    foreach ($selector in $item.Selectors) {
        Write-Host "    * $selector"
    }
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "Preview only. Re-run with -Apply to write changes."
    return
}

$backupRoot = Join-Path $PSScriptRoot ('.textbox-theme-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

$updatedFiles = 0

foreach ($item in $plannedChanges) {
    $path = $item.File
    $original = Get-Content -LiteralPath $path -Raw

    $updated = [regex]::Replace(
        $original,
        $stylePattern,
        {
            param($match)

            $selector = $match.Groups['selector'].Value
            $body = $match.Groups['body'].Value
            $open = $match.Groups['open'].Value
            $close = $match.Groups['close'].Value

            if (Test-IsTemplateBorderSelector -Selector $selector) {
                $body = Set-SetterValue -Body $body -Property 'Background' -Value $NewBackground
                return $open + $body.TrimEnd() + $close
            }

            if (-not (Test-IsBaseTextBoxSelector -Selector $selector)) {
                return $match.Value
            }

            $body = Set-SetterValue -Body $body -Property 'Background' -Value $NewBackground
            $body = Set-SetterValue -Body $body -Property 'Foreground' -Value $NewForeground
            $body = Set-SetterValue -Body $body -Property 'BorderBrush' -Value $NewBorderBrush
            $body = Set-SetterValue -Body $body -Property 'SelectionBrush' -Value $NewSelectionBrush
            $body = Set-SetterValue -Body $body -Property 'SelectionForegroundBrush' -Value $NewSelectionForeground
            $body = Set-SetterValue -Body $body -Property 'CaretBrush' -Value $NewCaretBrush

            return $open + $body.TrimEnd() + $close
        }
    )

    if ($updated -ne $original) {
        $relative = $path.Substring($ProjectPath.Length).TrimStart('\')
        $backupPath = Join-Path $backupRoot $relative
        $backupDir = Split-Path -Path $backupPath -Parent
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        Copy-Item -LiteralPath $path -Destination $backupPath -Force

        [System.IO.File]::WriteAllText($path, $updated, [System.Text.Encoding]::UTF8)
        $updatedFiles++
        Write-Host "Updated: $path"
    }
}

Write-Host ""
Write-Host "Backup folder: $backupRoot"
Write-Host "Files updated: $updatedFiles"

$axamlFiles = Get-ChildItem -Path $ProjectPath -Recurse -File -Filter '*.axaml' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts)\\' }

$remainingDark = $axamlFiles | Select-String -Pattern '#FF2E1B0C' -SimpleMatch -ErrorAction SilentlyContinue
$lightCount = $axamlFiles | Select-String -Pattern $NewBackground -SimpleMatch -ErrorAction SilentlyContinue

Write-Host "Remaining old background matches: $($remainingDark.Count)"
Write-Host "New background matches: $($lightCount.Count)"

