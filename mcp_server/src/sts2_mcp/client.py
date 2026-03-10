from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any
from urllib import error, request


@dataclass(slots=True)
class Sts2ApiError(RuntimeError):
    status_code: int
    code: str
    message: str
    details: Any = None
    retryable: bool = False

    def __str__(self) -> str:
        parts = [f"{self.code}: {self.message}", f"http={self.status_code}"]
        if self.retryable:
            parts.append("retryable=true")
        if self.details is not None:
            parts.append(f"details={json.dumps(self.details, ensure_ascii=False)}")
        return " | ".join(parts)


class Sts2Client:
    def __init__(self, base_url: str | None = None, timeout_seconds: float | None = None) -> None:
        self._base_url = (base_url or os.getenv("STS2_API_BASE_URL") or "http://127.0.0.1:8080").rstrip("/")
        self._timeout_seconds = timeout_seconds or float(os.getenv("STS2_API_TIMEOUT_SECONDS", "10"))

    @property
    def base_url(self) -> str:
        return self._base_url

    def get_health(self) -> dict[str, Any]:
        return self._request("GET", "/health")

    def get_state(self) -> dict[str, Any]:
        return self._request("GET", "/state")

    def get_available_actions(self) -> list[dict[str, Any]]:
        payload = self._request("GET", "/actions/available")
        return list(payload.get("actions", []))

    def end_turn(self) -> dict[str, Any]:
        return self.execute_action(
            "end_turn",
            client_context={
                "source": "mcp",
                "tool_name": "end_turn",
            },
        )

    def play_card(self, card_index: int, target_index: int | None = None) -> dict[str, Any]:
        return self.execute_action(
            "play_card",
            card_index=card_index,
            target_index=target_index,
            client_context={
                "source": "mcp",
                "tool_name": "play_card",
            },
        )

    def choose_map_node(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_map_node",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_map_node",
            },
        )

    def collect_rewards_and_proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "collect_rewards_and_proceed",
            client_context={
                "source": "mcp",
                "tool_name": "collect_rewards_and_proceed",
            },
        )

    def claim_reward(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "claim_reward",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "claim_reward",
            },
        )

    def choose_reward_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "choose_reward_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "choose_reward_card",
            },
        )

    def skip_reward_cards(self) -> dict[str, Any]:
        return self.execute_action(
            "skip_reward_cards",
            client_context={
                "source": "mcp",
                "tool_name": "skip_reward_cards",
            },
        )

    def select_deck_card(self, option_index: int) -> dict[str, Any]:
        return self.execute_action(
            "select_deck_card",
            option_index=option_index,
            client_context={
                "source": "mcp",
                "tool_name": "select_deck_card",
            },
        )

    def proceed(self) -> dict[str, Any]:
        return self.execute_action(
            "proceed",
            client_context={
                "source": "mcp",
                "tool_name": "proceed",
            },
        )

    def execute_action(
        self,
        action: str,
        *,
        card_index: int | None = None,
        target_index: int | None = None,
        option_index: int | None = None,
        client_context: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        return self._request(
            "POST",
            "/action",
            payload={
                "action": action,
                "card_index": card_index,
                "target_index": target_index,
                "option_index": option_index,
                "client_context": client_context,
            },
        )

    def _request(self, method: str, path: str, payload: dict[str, Any] | None = None) -> dict[str, Any]:
        raw_payload = None
        headers = {
            "Accept": "application/json",
        }

        if payload is not None:
            raw_payload = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json; charset=utf-8"

        http_request = request.Request(
            url=f"{self._base_url}{path}",
            method=method,
            data=raw_payload,
            headers=headers,
        )

        try:
            with request.urlopen(http_request, timeout=self._timeout_seconds) as response:
                return self._decode_success(response.read())
        except error.HTTPError as exc:
            self._raise_api_error(exc.code, exc.read())
        except error.URLError as exc:
            raise Sts2ApiError(
                status_code=0,
                code="connection_error",
                message=f"Failed to reach STS2 mod at {self._base_url}: {exc.reason}",
                retryable=True,
            ) from exc

        raise AssertionError("unreachable")

    @staticmethod
    def _decode_success(response_body: bytes) -> dict[str, Any]:
        payload = json.loads(response_body.decode("utf-8"))
        if not payload.get("ok", False):
            error_payload = payload.get("error", {})
            raise Sts2ApiError(
                status_code=200,
                code=error_payload.get("code", "unknown_error"),
                message=error_payload.get("message", "Request failed."),
                details=error_payload.get("details"),
                retryable=bool(error_payload.get("retryable", False)),
            )

        data = payload.get("data")
        if not isinstance(data, dict):
            raise Sts2ApiError(
                status_code=200,
                code="invalid_response",
                message="Server response did not contain an object data payload.",
                details=payload,
            )

        return data

    @staticmethod
    def _raise_api_error(status_code: int, response_body: bytes) -> None:
        try:
            payload = json.loads(response_body.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise Sts2ApiError(
                status_code=status_code,
                code="invalid_response",
                message="Server returned a non-JSON error response.",
            ) from exc

        error_payload = payload.get("error", {})
        raise Sts2ApiError(
            status_code=status_code,
            code=error_payload.get("code", "unknown_error"),
            message=error_payload.get("message", "Request failed."),
            details=error_payload.get("details"),
            retryable=bool(error_payload.get("retryable", False)),
        )
