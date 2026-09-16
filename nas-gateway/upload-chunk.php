<?php

require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'PUT' && $_SERVER['REQUEST_METHOD'] !== 'POST') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_upload_claims('upload');
$chunkIndex = isset($_GET['chunk_index']) ? $_GET['chunk_index'] : null;
if ($chunkIndex === null || !is_numeric($chunkIndex) || intval($chunkIndex) < 0 || intval($chunkIndex) > 2147483647) {
    projecthub_json_error(400, 'invalid_chunk_index');
}
$chunkIndex = intval($chunkIndex);
$sessionDir = projecthub_upload_session_dir($claims['upload_session_id']);
if (!is_dir($sessionDir) || is_link($sessionDir)) {
    projecthub_json_error(404, 'upload_session_not_found');
}

$input = fopen('php://input', 'rb');
if ($input === false) {
    projecthub_json_error(400, 'chunk_body_missing');
}
$partPath = $sessionDir . '/' . sprintf('%08d.part', $chunkIndex);
$tempPath = $partPath . '.tmp';
$output = @fopen($tempPath, 'wb');
if ($output === false) {
    fclose($input);
    projecthub_json_error(500, 'chunk_create_failed');
}
$written = 0;
while (!feof($input)) {
    $buffer = fread($input, 1048576);
    if ($buffer === false) { fclose($input); fclose($output); @unlink($tempPath); projecthub_json_error(500, 'chunk_read_failed'); }
    if ($buffer !== '') { $written += strlen($buffer); if (fwrite($output, $buffer) !== strlen($buffer)) { fclose($input); fclose($output); @unlink($tempPath); projecthub_json_error(500, 'chunk_write_failed'); } }
}
fflush($output);
fclose($input);
fclose($output);
if (!@rename($tempPath, $partPath)) {
    @unlink($tempPath);
    projecthub_json_error(500, 'chunk_commit_failed');
}

$total = 0;
foreach (glob($sessionDir . '/*.part') as $part) { if (is_file($part) && !is_link($part)) { $total += filesize($part); } }
echo json_encode(array('state' => 'uploading', 'upload_session_id' => $claims['upload_session_id'], 'chunk_index' => $chunkIndex, 'chunk_size_bytes' => $written, 'bytes_received' => $total));
