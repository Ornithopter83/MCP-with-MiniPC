<?php
require_once dirname(__FILE__) . '/common.php';
if ($_SERVER['REQUEST_METHOD'] !== 'GET') { projecthub_json_error(405, 'method_not_allowed'); }
header('X-ProjectHub-Download-Version: 20260918');
$claims = projecthub_upload_claims('download');
$hash = strtolower($claims['object_hash']);
$path = projecthub_canonical_object_path($hash);
$expectedSize = intval($claims['size_bytes']);
clearstatcache(true, $path);
if (!file_exists($path) || is_link($path) || !is_file($path)) {
    error_log('ProjectHub download operation=download error=object_not_found hash=' . $hash . ' path=' . $path);
    projecthub_json_error(404, 'object_not_found');
}
$actualSize = filesize($path);
if ($actualSize === false) {
    error_log('ProjectHub download operation=download error=object_size_unreadable hash=' . $hash . ' path=' . $path);
    projecthub_json_error(500, 'object_size_unreadable');
}
if (intval($actualSize) !== $expectedSize) {
    error_log('ProjectHub download operation=download error=object_size_mismatch hash=' . $hash . ' path=' . $path . ' expected=' . $expectedSize . ' actual=' . $actualSize);
    projecthub_json_error(409, 'object_size_mismatch');
}
if (!is_readable($path)) {
    error_log('ProjectHub download operation=download error=object_not_readable hash=' . $hash . ' path=' . $path . ' size=' . $actualSize);
    projecthub_json_error(500, 'object_not_readable');
}
$input = @fopen($path, 'rb');
if ($input === false) {
    error_log('ProjectHub download operation=download error=object_open_failed hash=' . $hash . ' path=' . $path . ' size=' . $actualSize);
    projecthub_json_error(500, 'object_open_failed');
}
header('X-ProjectHub-Download-Preflight: open-ok');
header('Content-Type: application/octet-stream');
header('Content-Length: ' . $actualSize);
$output = @fopen('php://output', 'wb');
$sent = $output === false ? false : @stream_copy_to_stream($input, $output);
fclose($input);
if ($output !== false) { fclose($output); }
if ($sent === false || intval($sent) !== intval($actualSize)) {
    error_log('ProjectHub download operation=download error=object_read_failed hash=' . $hash . ' path=' . $path . ' expected=' . $actualSize . ' sent=' . ($sent === false ? 'false' : $sent));
    if (!headers_sent()) { http_response_code(500); }
}
