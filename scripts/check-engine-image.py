#!/usr/bin/env python3
"""Run bounded real-UCI checks in a locally built worker image, without a database."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import queue
import subprocess
import threading
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    parser.add_argument("--platform", choices=("linux/amd64", "linux/arm64"), required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    prefix = ["docker", "--context", "default"]
    engine = subprocess.check_output([*prefix, "info", "--format", "{{.OSType}} {{.OperatingSystem}}"], text=True, timeout=10)
    if not engine.startswith("linux ") or "Docker Desktop" in engine:
        raise ValueError("A native Linux Docker Engine is required.")
    image = json.loads(subprocess.check_output([*prefix, "image", "inspect", args.image], text=True, timeout=10))[0]
    if f"{image['Os']}/{image['Architecture']}" != args.platform:
        raise ValueError("Image architecture does not match the explicitly selected platform.")
    name = f"chesslens-engine-check-{uuid.uuid4().hex}"
    process = subprocess.Popen([
        *prefix, "run", "--pull=never", "--rm", "-i", "--platform", args.platform,
        "--name", name,
        "--network", "none", "--read-only", "--cap-drop", "ALL", "--memory", "512m", "--cpus", "1",
        "--entrypoint", "/opt/stockfish/stockfish", args.image],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    lines = queue.Queue(maxsize=2048)
    transcript = []

    def read():
        for line in process.stdout:
            if len(line) > 65536:
                lines.put("PROTOCOL_LINE_TOO_LONG")
                break
            lines.put(line.rstrip())
        lines.put(None)

    threading.Thread(target=read, daemon=True).start()

    def send(command):
        process.stdin.write(command + "\n")
        process.stdin.flush()

    def until(predicate, seconds=45):
        deadline = time.monotonic() + seconds
        result = []
        while time.monotonic() < deadline:
            line = lines.get(timeout=max(0.1, deadline - time.monotonic()))
            if line is None or line == "PROTOCOL_LINE_TOO_LONG" or "CRITICAL ERROR" in line:
                raise RuntimeError("Engine exited or rejected the bounded protocol check.")
            transcript.append(line)
            result.append(line)
            if predicate(line):
                return result
        raise TimeoutError("Engine protocol deadline exceeded.")

    checks = []
    try:
        send("uci")
        until(lambda line: line == "uciok")
        send("setoption name Hash value 16")
        send("setoption name Threads value 1")
        send("setoption name MultiPV value 3")
        send("isready")
        until(lambda line: line == "readyok")
        positions = [
            ("completed-game missed black mate", "startpos moves f2f3 e7e5 g2g4", "d8h4"),
            ("white promotion mate", "fen 7k/P7/6K1/8/8/8/8/8 w - - 0 1", None),
            ("terminal promoted rook mate", "fen R6k/8/6K1/8/8/8/8/8 b - - 0 1", "(none)"),
        ]
        for label, position, expected in positions:
            send("ucinewgame")
            send("position " + position)
            send("go nodes 20000")
            result = until(lambda line: line.startswith("bestmove "))
            move = result[-1].split()[1]
            if expected and move != expected:
                raise ValueError(f"Unexpected best move for {label}: {move}.")
            if label == "white promotion mate" and move not in ("a7a8q", "a7a8r"):
                raise ValueError("Promotion fixture did not return a legal mating promotion.")
            if move not in ("0000", "(none)") and not any("score mate 1" in line for line in result):
                raise ValueError("The mating fixture lacks the expected real mate score.")
            checks.append({"position": label, "bestMove": move, "mateScoreObserved": any("score mate 1" in line for line in result)})
        send("quit")
        process.wait(timeout=10)
        if process.returncode:
            raise RuntimeError("Engine did not shut down cleanly.")
        receipt = {"testedUtc": datetime.now(timezone.utc).isoformat(), "image": args.image,
                   "imageId": image["Id"], "platform": args.platform, "enginePath": "/opt/stockfish/stockfish",
                   "settings": {"threads": 1, "hashMiB": 16, "multiPv": 3, "nodes": 20000},
                   "checks": checks, "exitCode": process.returncode,
                   "scope": "Real bounded UCI in the selected worker image; no application/DB or throughput claim.",
                   "transcript": transcript}
        args.output.write_text(json.dumps(receipt, indent=2) + "\n")
        print(f"Passed {len(checks)} real UCI checks in {args.platform}; engine shut down cleanly.")
    finally:
        if process.poll() is None:
            try:
                send("quit")
                process.wait(timeout=5)
            except (OSError, subprocess.TimeoutExpired):
                process.kill()
                process.wait(timeout=10)
        subprocess.run([*prefix, "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)


if __name__ == "__main__":
    main()
