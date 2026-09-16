import base64
import json
import time
import uuid

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding


PRIVATE_KEY = "projecthub-private.pem"


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


now = int(time.time())

header = {
    "alg": "RS256",
    "typ": "JWT"
}

payload = {
    "v": 1,
    "iss": "projecthub-server",
    "aud": "projecthub-nas-gateway",
    "sub": "DEV-PC-01",

    "project_id": "projecthub-test",
    "workstation_id": "DEV-PC-01",

    "operation": "provision",
    "storage_scope": "ProjectHub",

    "iat": now,
    "exp": now + 600,
    "jti": str(uuid.uuid4())
}


encoded_header = b64url(
    json.dumps(header, separators=(",", ":")).encode("utf-8")
)

encoded_payload = b64url(
    json.dumps(payload, separators=(",", ":")).encode("utf-8")
)

signing_input = (
    encoded_header + "." + encoded_payload
).encode("ascii")


with open(PRIVATE_KEY, "rb") as f:
    private_key = serialization.load_pem_private_key(
        f.read(),
        password=None
    )


signature = private_key.sign(
    signing_input,
    padding.PKCS1v15(),
    hashes.SHA256()
)


token = (
    encoded_header
    + "."
    + encoded_payload
    + "."
    + b64url(signature)
)

print(token)