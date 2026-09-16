<?php

require_once dirname(__FILE__) . '/common.php';
header('Content-Type: application/json; charset=utf-8');

if ($_SERVER['REQUEST_METHOD'] !== 'GET') {
    projecthub_json_error(405, 'method_not_allowed');
}

$claims = projecthub_upload_claims('upload');
$sessionDir = projecthub_upload_session_dir($claims['upload_session_id']);
if (!is_dir($sessionDir) || is_link($sessionDir)) {
    projecthub_json_error(404, 'upload_session_not_found');
}
$chunks = array();
$total = 0;
foreach (glob($sessionDir . '/*.part') as $part) {
    if (!is_file($part) || is_link($part)) { continue; }
    $name = basename($part, '.part');
    $chunks[] = intval($name);
    $total += filesize($part);
}
sort($chunks, SORT_NUMERIC);
echo json_encode(array('state' => 'uploading', 'upload_session_id' => $claims['upload_session_id'], 'bytes_received' => $total, 'completed_chunks' => $chunks));
