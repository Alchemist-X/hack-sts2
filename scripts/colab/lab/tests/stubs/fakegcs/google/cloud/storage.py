"""Fake google.cloud.storage over a directory ($FAKE_GCS_ROOT/<bucket>/<object>) for the GcsStore helper test."""
import base64, hashlib, os, shutil


class Blob:
    def __init__(self, bucket, name):
        self.bucket, self.name = bucket, name
        self.path = os.path.join(bucket.root, name)

    @property
    def size(self):
        return os.path.getsize(self.path)

    @property
    def md5_hash(self):
        return base64.b64encode(hashlib.md5(open(self.path, "rb").read()).digest()).decode()

    def download_as_bytes(self):
        return open(self.path, "rb").read()

    def download_to_filename(self, dest):
        shutil.copyfile(self.path, dest)

    def upload_from_filename(self, src, timeout=None):  # noqa: ARG002
        os.makedirs(os.path.dirname(self.path), exist_ok=True)
        shutil.copyfile(src, self.path)


class Bucket:
    def __init__(self, client, name):
        self.client, self.name = client, name
        self.root = os.path.join(os.environ["FAKE_GCS_ROOT"], name)

    def blob(self, name):
        return Blob(self, name)

    def get_blob(self, name):
        b = Blob(self, name)
        return b if os.path.isfile(b.path) else None


class Client:
    @classmethod
    def from_service_account_json(cls, key):  # noqa: ARG003
        return cls()

    def bucket(self, name):
        return Bucket(self, name)

    def list_blobs(self, bucket, prefix=""):
        b = Bucket(self, bucket)
        out = []
        for root, _, files in os.walk(b.root):
            for f in files:
                rel = os.path.relpath(os.path.join(root, f), b.root).replace(os.sep, "/")
                if rel.startswith(prefix):
                    out.append(Blob(b, rel))
        return sorted(out, key=lambda x: x.name)
