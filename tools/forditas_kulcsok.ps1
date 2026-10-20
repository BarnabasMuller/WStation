# A felület magyar szövegeinek kigyűjtése; megmutatja nyelvenként, mi fordítatlan, és frissíti a web\lang\_sablon.json-t.
# Használat: powershell -ExecutionPolicy Bypass -File tools\forditas_kulcsok.ps1 [-Out <mappa a hiánylistáknak>]
param([string]$Root = (Split-Path -Parent $PSScriptRoot), [string]$Out)
# Magyar szövegdarabok kigyűjtése a webes és a C# forrásokból (fordításhoz)
$files = @()
$files += Get-ChildItem (Join-Path $Root 'web') -Include *.js,*.html -Recurse | Where-Object { $_.Name -notlike 'echarts*' -and $_.Name -notlike 'i18n*' }
$files += Get-ChildItem (Join-Path $Root 'src\megjelenito') -Filter *.cs | Where-Object { $_.Name -ne 'SelfTest.cs' }
$set = New-Object 'System.Collections.Generic.HashSet[string]'
$hu = '[áéíóöőúüűÁÉÍÓÖŐÚÜŰ]|\b(nap|napi|óra|perc|és|vagy|nincs|szél|eső|hét|hónap|év|Ma|ma|igen|nem|ki|be|alap|tól|ig|Mentés|Törlés|Betöltés|Alkalmaz|Mégse|Bezárás|Program|Beállítások|Statisztika|Futás|Jelentés|Naptár|Szárazság|Idővonal|Áttekintés|Eső|Szél)\b'
foreach ($f in $files) {
  $t = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
  # kommentek ki
  $t = [regex]::Replace($t, '(?m)^\s*//.*$', '')
  $t = [regex]::Replace($t, '/\*[\s\S]*?\*/', '')
  $lits = [regex]::Matches($t, "'((?:[^'\\\r\n]|\\.){1,})'|`"((?:[^`"\\\r\n]|\\.){1,})`"|``([^``]*)``")
  $chunks = @()
  foreach ($m in $lits) { $chunks += ($m.Groups[1].Value + $m.Groups[2].Value + $m.Groups[3].Value) }
  if ($f.Extension -eq '.html') { $chunks += $t }
  foreach ($c in $chunks) {
    $c = [regex]::Replace($c, '\$\{[^}]*\}', '|')
    $c = $c -replace '\\n', '|' -replace '\\"', '"' -replace "\\'", "'"
    # attribútumokból a title / placeholder szövege
    foreach ($a in [regex]::Matches($c, '(?:title|placeholder|aria-label)="([^"]+)"')) { $chunks2 = $a.Groups[1].Value; [void]$set.Add($chunks2.Trim()) }
    $c = [regex]::Replace($c, '<[^>]*>', '|')
    $c = [regex]::Replace($c, '&nbsp;|&lt;|&gt;|&amp;', ' ')
    foreach ($p in ($c -split '\|')) {
      $s = $p.Trim()
      $s = $s.Trim(' ', '·', ':', ',', ';', '(', ')', '–', '-', '—', '→', '.', '!', '?', '+', '=', '/', '*', '•', '⚠', '✔', '"', "'")
      $s = $s.Trim()
      if ($s.Length -lt 2) { continue }
      if ($s -notmatch $hu) { continue }
      if ($s -match '^[\w\.\-]+\.(js|json|ini|csv|cs|exe|zip|txt)$') { continue }
      if ($s -match '^(font-|var\(|#|rgba?\(|[a-z]+:[a-z])') { continue }
      [void]$set.Add($s)
    }
  }
}
# --- összevetés a meglévő nyelvi fájlokkal, a sablon frissítése
Add-Type -AssemblyName System.Web.Extensions
$js = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$js.MaxJsonLength = 50000000
$langDir = Join-Path $Root 'web\lang'
$all = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
# a {szo} helyőrzős mondatsablonok (tf) megmaradnak, más kapcsos zárójel nem
foreach ($k in $set) { if ($k -notmatch '[<>]|="' -and ($k -replace '\{\w+\}', '') -notmatch '[{}]') { [void]$all.Add(($k -replace '\s{2,}', ' ')) } }
foreach ($f in Get-ChildItem $langDir -Filter '*.json' | Where-Object { $_.Name -notlike '_*' }) {
  $d = $js.DeserializeObject([IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8))
  foreach ($k in $d.Keys) { if ($k -ne '_meta') { [void]$all.Add($k) } }
}
foreach ($f in Get-ChildItem $langDir -Filter '*.json' | Where-Object { $_.Name -notlike '_*' }) {
  $d = $js.DeserializeObject([IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8))
  $miss = @($all | Where-Object { -not $d.ContainsKey($_) -or -not $d[$_] })
  "{0}: {1} kifejezés, ebből {2} fordítatlan" -f $f.BaseName, $all.Count, $miss.Count
  if ($Out) { [IO.File]::WriteAllLines((Join-Path $Out ($f.BaseName + '_hianyzik.txt')), $miss, (New-Object Text.UTF8Encoding($false))) }
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("{`n  `"_meta`": { `"code`": `"xx`", `"name`": `"A nyelv neve a saját nyelvén`", `"locale`": `"xx-XX`", `"dirs`": [`"N`",`"NNE`",`"NE`",`"ENE`",`"E`",`"ESE`",`"SE`",`"SSE`",`"S`",`"SSW`",`"SW`",`"WSW`",`"W`",`"WNW`",`"NW`",`"NNW`"], `"files`": { `"viewer`": `"A Megjelenítő exe neve ezen a nyelven, pl. Viewer.exe`", `"downloader`": `"A Letöltő exe neve, pl. Downloader.exe`" } }")
foreach ($k in $all) { [void]$sb.Append(",`n  " + $js.Serialize($k) + ": `"`"") }
[void]$sb.Append("`n}`n")
$o = [regex]::Replace($sb.ToString(), '\\u([0-9a-fA-F]{4})', { param($m) $c = [char][Convert]::ToInt32($m.Groups[1].Value, 16); if ($c -eq '"' -or $c -eq '\' -or [int]$c -lt 32) { $m.Value } else { [string]$c } })
[IO.File]::WriteAllText((Join-Path $langDir '_sablon.json'), $o, (New-Object Text.UTF8Encoding($false)))
"_sablon.json frissítve: " + $all.Count + " kulcs"