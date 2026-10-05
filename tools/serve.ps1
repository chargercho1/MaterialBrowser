$root = 'G:\MaterialBrowser\out'
$port = 8137
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$port/")
$listener.Start()
while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $name = [System.Uri]::UnescapeDataString($ctx.Request.Url.AbsolutePath.TrimStart('/').Replace('/', '\'))
    if ([string]::IsNullOrEmpty($name)) { $name = 'view.html' }
    $file = Join-Path $root $name
    try {
        if (Test-Path $file -PathType Leaf) {
            $bytes = [System.IO.File]::ReadAllBytes($file)
            if ($file.EndsWith('.html')) { $ctx.Response.ContentType = 'text/html; charset=utf-8' }
            elseif ($file.EndsWith('.png')) { $ctx.Response.ContentType = 'image/png' }
            elseif ($file.EndsWith('.txt')) { $ctx.Response.ContentType = 'text/plain; charset=utf-8' }
            else { $ctx.Response.ContentType = 'application/octet-stream' }
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $ctx.Response.StatusCode = 404
            $ctx.Response.Close()
        }
    } catch { $ctx.Response.StatusCode = 500; $ctx.Response.Close() }
}