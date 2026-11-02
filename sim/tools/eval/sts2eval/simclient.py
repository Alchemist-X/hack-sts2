"""Small async client for the Sts2Sim play server (sim/PLAY.md), on the SDK's httpx2."""

from __future__ import annotations

import json

import httpx2


class SimError(Exception):
    """Unexpected HTTP status or unreachable server."""

    def __init__(self, status: int | None, body: dict | None, message: str | None = None):
        self.status = status
        self.body = body or {}
        super().__init__(message or self.body.get("error") or f"HTTP {status}")


class SimIllegal(SimError):
    """400: illegal token (game unchanged)."""


class SimCrash(SimError):
    """500: the simulator threw; the game is dead."""


class SimClient:
    def __init__(self, url: str, timeout: float = 60.0, max_connections: int = 64):
        self.url = url.rstrip("/")
        self._http = httpx2.AsyncClient(
            base_url=self.url, timeout=timeout,
            limits=httpx2.Limits(max_connections=max_connections, max_keepalive_connections=max_connections))

    async def aclose(self) -> None:
        await self._http.aclose()

    async def _req(self, method: str, path: str, body: dict | None = None) -> dict:
        try:
            r = await self._http.request(method, path, content=None if body is None else json.dumps(body),
                                         headers={"Content-Type": "application/json"})
        except httpx2.HTTPError as e:
            raise SimError(None, None, f"server not reachable at {self.url}: {type(e).__name__}: {e}") from None
        try:
            data = r.json() if r.content else {}
        except ValueError:
            data = {"error": r.text[:2000]}
        if r.status_code == 200:
            return data
        if r.status_code == 400:
            raise SimIllegal(400, data)
        if r.status_code == 500:
            raise SimCrash(500, data)
        raise SimError(r.status_code, data)

    async def health(self) -> dict:
        return await self._req("GET", "/health")

    async def new_run(self, seed, character: str, ascension: int, content: str) -> dict:
        return await self._req("POST", "/runs", {"seed": seed, "character": character,
                                                 "ascension": ascension, "content": content})

    async def show(self, gid: str) -> dict:
        return await self._req("GET", f"/games/{gid}")

    async def step(self, gid: str, token: str) -> dict:
        return await self._req("POST", f"/games/{gid}/step", {"token": token})

    async def history(self, gid: str) -> dict:
        return await self._req("GET", f"/games/{gid}/history")

    async def delete(self, gid: str) -> dict:
        return await self._req("DELETE", f"/games/{gid}")
