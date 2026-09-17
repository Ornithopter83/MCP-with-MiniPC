<?php

function projecthub_json_error($status, $error)
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');

    echo json_encode(array(
        'error' => $error
    ));

    exit;
}

function projecthub_get_authorization_header()
{
    if (isset($_SERVER['HTTP_AUTHORIZATION'])) {
        return $_SERVER['HTTP_AUTHORIZATION'];
    }

    if (isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
        return $_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
    }

    if (function_exists('getallheaders')) {
        $headers = getallheaders();

        foreach ($headers as $name => $value) {
            if (strtolower($name) === 'authorization') {
                return $value;
            }
        }
    }

    if (function_exists('apache_request_headers')) {
        $headers = apache_request_headers();

        foreach ($headers as $name => $value) {
            if (strtolower($name) === 'authorization') {
                return $value;
            }
        }
    }

    return '';
}

function projecthub_base64url_decode($value)
{
    $value = str_replace(array('-', '_'), array('+', '/'), $value);

    $padding = strlen($value) % 4;

    if ($padding > 0) {
        $value .= str_repeat('=', 4 - $padding);
    }

    return base64_decode($value);
}

function projecthub_validate_identifier($value)
{
    if (!is_string($value)) {
        return false;
    }

    if ($value === '') {
        return false;
    }

    if (strlen($value) > 128) {
        return false;
    }

    return preg_match('/^[A-Za-z0-9._-]+$/', $value) === 1;
}

function projecthub_validate_storage_scope($scope)
{
    if (!is_string($scope) || $scope === '') {
        return false;
    }

    if (strpos($scope, '\\') !== false) {
        return false;
    }

    if (strpos($scope, ':') !== false) {
        return false;
    }

    if (substr($scope, 0, 1) === '/') {
        return false;
    }

    $parts = explode('/', $scope);

    foreach ($parts as $part) {
        if ($part === '' || $part === '.' || $part === '..') {
            return false;
        }

        if (!preg_match('/^[A-Za-z0-9._-]+$/', $part)) {
            return false;
        }
    }

    return true;
}

function projecthub_verify_assertion($requiredOperation)
{
    $authorization = projecthub_get_authorization_header();

    if (substr($authorization, 0, 7) !== 'Bearer ') {
        projecthub_json_error(401, 'upload_session_required');
    }

    $token = substr($authorization, 7);

    $parts = explode('.', $token);

    if (count($parts) !== 3) {
        projecthub_json_error(401, 'invalid_assertion');
    }

    $encodedHeader = $parts[0];
    $encodedPayload = $parts[1];
    $encodedSignature = $parts[2];

    $headerJson = projecthub_base64url_decode($encodedHeader);
    $payloadJson = projecthub_base64url_decode($encodedPayload);
    $signature = projecthub_base64url_decode($encodedSignature);

    if ($headerJson === false || $payloadJson === false || $signature === false) {
        projecthub_json_error(401, 'invalid_assertion');
    }

    $header = json_decode($headerJson, true);
    $payload = json_decode($payloadJson, true);

    if (!is_array($header) || !is_array($payload)) {
        projecthub_json_error(401, 'invalid_assertion');
    }

    if (!isset($header['alg']) || $header['alg'] !== 'RS256') {
        projecthub_json_error(401, 'invalid_algorithm');
    }

    $publicKeyPath = dirname(__FILE__) . '/keys/projecthub-public.pem';

    if (!file_exists($publicKeyPath)) {
        projecthub_json_error(500, 'public_key_missing');
    }

    $publicKeyPem = file_get_contents($publicKeyPath);

    if ($publicKeyPem === false) {
        projecthub_json_error(500, 'public_key_read_failed');
    }

    $publicKey = openssl_pkey_get_public($publicKeyPem);

    if ($publicKey === false) {
        projecthub_json_error(500, 'public_key_invalid');
    }

    $signedData = $encodedHeader . '.' . $encodedPayload;

    $verifyResult = openssl_verify(
        $signedData,
        $signature,
        $publicKey,
        OPENSSL_ALGO_SHA256
    );

    if (function_exists('openssl_free_key')) {
        openssl_free_key($publicKey);
    }

    if ($verifyResult !== 1) {
        projecthub_json_error(401, 'invalid_signature');
    }

    if (!isset($payload['v']) || intval($payload['v']) !== 1) {
        projecthub_json_error(401, 'invalid_version');
    }

    if (!isset($payload['iss']) || $payload['iss'] !== 'projecthub-server') {
        projecthub_json_error(401, 'invalid_issuer');
    }

    if (!isset($payload['aud']) || $payload['aud'] !== 'projecthub-nas-gateway') {
        projecthub_json_error(401, 'invalid_audience');
    }

    $now = time();

    if (!isset($payload['iat']) || !is_numeric($payload['iat'])) {
        projecthub_json_error(401, 'invalid_iat');
    }

    if (!isset($payload['exp']) || !is_numeric($payload['exp'])) {
        projecthub_json_error(401, 'invalid_exp');
    }

    if (intval($payload['iat']) > $now + 300) {
        projecthub_json_error(401, 'iat_in_future');
    }

    if (intval($payload['exp']) <= $now) {
        projecthub_json_error(401, 'assertion_expired');
    }

    if (intval($payload['exp']) - intval($payload['iat']) > 3600) {
        projecthub_json_error(401, 'assertion_lifetime_too_long');
    }

    if (!isset($payload['operation']) || $payload['operation'] !== $requiredOperation) {
        projecthub_json_error(403, 'operation_not_allowed');
    }

    if (!isset($payload['project_id']) ||
        !projecthub_validate_identifier($payload['project_id'])) {
        projecthub_json_error(401, 'invalid_project_id');
    }

    if (!isset($payload['workstation_id']) ||
        !projecthub_validate_identifier($payload['workstation_id'])) {
        projecthub_json_error(401, 'invalid_workstation_id');
    }

    if (!isset($payload['jti']) ||
        !projecthub_validate_identifier($payload['jti'])) {
        projecthub_json_error(401, 'invalid_jti');
    }

    if (!isset($payload['storage_scope']) ||
        !projecthub_validate_storage_scope($payload['storage_scope'])) {
        projecthub_json_error(401, 'invalid_storage_scope');
    }

    return $payload;
}

function projecthub_json_body()
{
    $raw = file_get_contents('php://input');
    if ($raw === false || $raw === '') {
        return array();
    }

    $body = json_decode($raw, true);
    if (!is_array($body)) {
        projecthub_json_error(400, 'invalid_json');
    }

    return $body;
}

function projecthub_validate_sha256($value)
{
    return is_string($value) && preg_match('/^[a-fA-F0-9]{64}$/', $value) === 1;
}

function projecthub_validate_session_id($value)
{
    return projecthub_validate_identifier($value) && strlen($value) <= 96;
}

function projecthub_storage_root()
{
    return '/mnt/HDD1/ProjectHub';
}

function projecthub_safe_path($root, $relative)
{
    $root = rtrim($root, '/');
    $path = $root . '/' . ltrim($relative, '/');
    $parent = dirname($path);
    if (strpos($path, $root . '/') !== 0 || strpos($relative, '..') !== false) {
        projecthub_json_error(400, 'storage_path_invalid');
    }
    if (file_exists($parent) && is_link($parent)) {
        projecthub_json_error(500, 'symlink_not_allowed');
    }
    return $path;
}

function projecthub_upload_claims($requiredOperation)
{
    $claims = projecthub_verify_assertion($requiredOperation);
    if (!isset($claims['upload_session_id']) || !projecthub_validate_session_id($claims['upload_session_id'])) {
        projecthub_json_error(401, 'invalid_upload_session_id');
    }
    if (!isset($claims['object_hash']) || !projecthub_validate_sha256($claims['object_hash'])) {
        projecthub_json_error(401, 'invalid_object_hash');
    }
    if (!isset($claims['size_bytes']) || !is_numeric($claims['size_bytes']) || intval($claims['size_bytes']) < 0) {
        projecthub_json_error(401, 'invalid_size_bytes');
    }
    return $claims;
}

function projecthub_upload_session_dir($sessionId)
{
    return projecthub_safe_path(projecthub_storage_root(), 'staging/' . $sessionId);
}

function projecthub_validate_relative_path($value)
{
    if (!is_string($value) || $value === '' || strpos($value, '\\') !== false || strpos($value, "\0") !== false || substr($value, 0, 1) === '/') {
        return false;
    }
    foreach (explode('/', $value) as $part) {
        if ($part === '' || $part === '.' || $part === '..') { return false; }
    }
    return true;
}

function projecthub_named_object_path($claims, $objectHash)
{
    if (!isset($claims['relative_path']) || !projecthub_validate_relative_path($claims['relative_path'])) {
        return null;
    }
    $relative = 'files/' . $claims['project_id'] . '/' . $claims['relative_path'];
    return projecthub_safe_path(projecthub_storage_root(), $relative);
}

function projecthub_cleanup_session($sessionDir, $parts = array())
{
    foreach ($parts as $part) { if (is_file($part) && !is_link($part)) { @unlink($part); } }
    @unlink($sessionDir . '/assembled.tmp');
    @unlink($sessionDir . '/session.json');
    return @rmdir($sessionDir);
}

function projecthub_create_named_alias($claims, $objectPath, $objectHash)
{
    $namedPath = projecthub_named_object_path($claims, $objectHash);
    if ($namedPath === null) { return null; }
    $parent = dirname($namedPath);
    if (!is_dir($parent) && !@mkdir($parent, 0750, true)) { projecthub_json_error(500, 'named_directory_create_failed'); }
    $cursor = $parent;
    $root = rtrim(projecthub_storage_root(), '/');
    while ($cursor !== $root) {
        if (is_link($cursor)) { projecthub_json_error(500, 'named_path_symlink_not_allowed'); }
        $cursor = dirname($cursor);
    }
    if (file_exists($namedPath)) {
        if (is_link($namedPath) || !is_file($namedPath) || strtolower(hash_file('sha256', $namedPath)) !== strtolower($objectHash)) { projecthub_json_error(409, 'named_path_conflict'); }
        return 'files/' . $claims['project_id'] . '/' . $claims['relative_path'];
    }
    if (!@link($objectPath, $namedPath)) { projecthub_json_error(500, 'named_alias_create_failed'); }
    return 'files/' . $claims['project_id'] . '/' . $claims['relative_path'];
}
