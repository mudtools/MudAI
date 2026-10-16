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
    $comments = 0; $maxBlock = 0; $run = 0
    foreach ($s in $lines) {
        $t = $s.TrimStart()
        if (-not $t.StartsWith('//')) { $run = 0; continue }
        $comments++
        if ($t.StartsWith('/// <param') -or $t.StartsWith('/// <typeparam')) { $run = 0; continue }
        $run++; if ($run -gt $maxBlock) { $maxBlock = $run }
    }
    "{0}: lines={1} comments={2} maxBlock={3}" -f $rel, $lines.Count, $comments, $maxBlock
}
