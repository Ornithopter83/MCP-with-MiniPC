<?php

require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_upload_claims('upload');
$sessionDir = projecthub_upload_session_dir($claims['upload_session_id']);
if (!is_dir($sessionDir) || is_link($sessionDir)) {
    projecthub_json_error(404, 'upload_session_not_found');
}
$parts = glob($sessionDir . '/*.part');
usort($parts, function ($left, $right) { return strcmp(basename($left), basename($right)); });
$assembled = $sessionDir . '/assembled.tmp';
$output = @fopen($assembled, 'wb');
if ($output === false) { projecthub_json_error(500, 'finalize_create_failed'); }
foreach ($parts as $part) {
    if (!is_file($part) || is_link($part)) { fclose($output); @unlink($assembled); projecthub_json_error(400, 'invalid_chunk'); }
    $input = fopen($part, 'rb');
    while (!feof($input)) { $buffer = fread($input, 1048576); if ($buffer === false) { fclose($input); fclose($output); @unlink($assembled); projecthub_json_error(500, 'finalize_read_failed'); } if ($buffer !== '' && fwrite($output, $buffer) !== strlen($buffer)) { fclose($input); fclose($output); @unlink($assembled); projecthub_json_error(500, 'finalize_write_failed'); } }
    fclose($input);
}
fflush($output);
fclose($output);
clearstatcache(true, $assembled);
$size = filesize($assembled);
$hash = strtolower(hash_file('sha256', $assembled));
if ($size !== intval($claims['size_bytes'])) { @unlink($assembled); projecthub_json_error(409, 'size_mismatch'); }
if ($hash !== strtolower($claims['object_hash'])) { @unlink($assembled); projecthub_json_error(409, 'hash_mismatch'); }
$objectPath = projecthub_safe_path(projecthub_storage_root(), 'objects/sha256/' . substr($hash, 0, 2) . '/' . $hash);
if (file_exists($objectPath)) { @unlink($assembled); @rmdir($sessionDir); echo json_encode(array('state' => 'complete', 'already_present' => true, 'object_hash' => $hash, 'size_bytes' => $size)); exit; }
$parent = dirname($objectPath);
if (!is_dir($parent) && !@mkdir($parent, 0750, true)) { @unlink($assembled); projecthub_json_error(500, 'object_directory_create_failed'); }
if (!@rename($assembled, $objectPath)) { @unlink($assembled); projecthub_json_error(500, 'object_commit_failed'); }
foreach ($parts as $part) { @unlink($part); }
@unlink($sessionDir . '/session.json');
@rmdir($sessionDir);
echo json_encode(array('state' => 'complete', 'already_present' => false, 'object_hash' => $hash, 'size_bytes' => $size));
