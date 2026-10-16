param([int]$MinBlock = 6)
$root = 'd:\Repos\mudai'
$files = @(
    'Mud.AI.Agent/Conversations/ConversationSummarizer.cs',
    'Mud.AI.Generator/ToolSurface/SdkToolProfileModel.cs',
    'Mud.AI.Agent/Events/ConversationalTurnPipeline.cs',
    'Mud.AI.Generator/ToolSurface/Extraction/ParameterSchemaRenderer.cs',
    'Mud.AI.Generator/ToolSurface/ToolSurfaceSourceGenerator.cs',
    'Mud.AI.Generator/ToolSurface/Emit/ToolArgsEmitter.cs'
)
foreach ($rel in $files) {
    $lines = [System.IO.File]::ReadAllLines((Join-Path $root $rel))
    $i = 0
    "{0}" -f $rel
    while ($i -lt $lines.Count) {
        $t = $lines[$i].TrimStart()
        if (-not $t.StartsWith('//')) { $i++; continue }
        $start = $i
        $len = 0
        while ($i -lt $lines.Count) {
            $u = $lines[$i].TrimStart()
            if (-not $u.StartsWith('//')) { break }
            $len++
            if ($u.StartsWith('/// <param') -or $u.StartsWith('/// <typeparam')) { $i++; $len = 0; $start = $i; continue }
            $i++
        }
        if ($len -ge $MinBlock) {
            $head = $lines[$start].Trim()
            if ($head.Length -gt 70) { $head = $head.Substring(0, 70) }
            "   L{0,4} len={1,3}  {2}" -f ($start + 1), $len, $head
        }
    }
}
