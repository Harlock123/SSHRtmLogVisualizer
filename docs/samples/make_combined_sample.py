#!/usr/bin/env python3
"""Writes combined-20261106-141500.csv: a made-up SSHRTM combined log of three pretend servers.

Ten minutes at one row a second, laid out as SSHRtm's CombinedCsvLogger writes it (timestamp first, then each
server's status, sample age and columns prefixed "server::"; process slots left out, as with
"combinedLogProcesses": false). db01 drops out for 45 seconds partway through, so its cells are empty there.
Standard library only; the same output every run.
"""
import math
import random
from datetime import datetime, timedelta
from pathlib import Path

ROWS = 600
START = datetime(2026, 11, 6, 14, 15, 0)
OUTAGE = range(330, 375)  # db01 is unreachable for these rows

SERVERS = [
    # name, cores, NICs, disks, mounts, memory total, base CPU %, CPU swing
    ("web01", 4, ["eth0"], ["sda"], ["/"], 16 * 2**30, 35, 25),
    ("db01", 8, ["eth0", "eth1"], ["sda", "sdb"], ["/", "/data"], 64 * 2**30, 55, 20),
    ("cache01", 2, ["eth0"], ["sda"], ["/"], 8 * 2**30, 15, 10),
]


def columns(name, cores, nics, disks, mounts):
    cols = ["status", "sample_age_s", "hostname", "num_cpu", "uptime_sec", "load_1m", "load_5m", "load_15m",
            "cpu_overall_pct"]
    cols += [f"cpu{i}_pct" for i in range(cores)]
    cols += ["mem_total_bytes", "mem_used_bytes", "mem_available_bytes", "mem_cached_bytes", "mem_buffers_bytes",
             "mem_used_pct", "swap_total_bytes", "swap_used_bytes", "swap_used_pct"]
    for n in nics:
        cols += [f"net_{n}_rx_bps", f"net_{n}_tx_bps", f"net_{n}_rx_pps", f"net_{n}_tx_pps"]
    for d in disks:
        cols += [f"disk_{d}_read_bps", f"disk_{d}_write_bps", f"disk_{d}_busy_pct", f"disk_{d}_inflight"]
    for m in mounts:
        cols += [f"fs_{m}_type", f"fs_{m}_total_kb", f"fs_{m}_used_kb", f"fs_{m}_avail_kb", f"fs_{m}_used_pct"]
    return [f"{name}::{c}" for c in cols]


def cells(rng, row, name, cores, nics, disks, mounts, mem_total, base, swing):
    t = row / 60.0
    wave = math.sin(t * 2.1 + len(name)) * 0.5 + 0.5
    cpu = min(99.0, max(0.5, base + swing * wave + rng.uniform(-6, 6)))
    out = ["Ok", f"{rng.uniform(0.05, 0.9):.2f}", f"{name}.example.com", str(cores), f"{86400 * 12 + row:.1f}"]
    load = cpu / 100 * cores
    out += [f"{load:.2f}", f"{load * 0.9:.2f}", f"{load * 0.8:.2f}", f"{cpu:.2f}"]
    out += [f"{min(100.0, max(0.0, cpu + rng.uniform(-15, 15))):.2f}" for _ in range(cores)]
    used = int(mem_total * (0.45 + 0.2 * wave + rng.uniform(-0.01, 0.01)))
    cached = int(mem_total * 0.25)
    out += [str(mem_total), str(used), str(mem_total - used), str(cached), str(mem_total // 64),
            f"{used / mem_total * 100:.2f}", str(4 * 2**30), str(2**20 * 12), "0.29"]
    for i, _ in enumerate(nics):
        rx = (2e6 + 6e6 * wave) / (i + 1) * rng.uniform(0.8, 1.2)
        tx = rx * 0.6
        out += [f"{rx:.2f}", f"{tx:.2f}", f"{rx / 900:.2f}", f"{tx / 900:.2f}"]
    for i, _ in enumerate(disks):
        rd = (1e6 + 4e6 * wave) / (i + 1) * rng.uniform(0.7, 1.3)
        wr = rd * 1.4
        out += [f"{rd:.2f}", f"{wr:.2f}", f"{min(100, cpu * 0.6 + rng.uniform(-5, 5)):.2f}", str(rng.randint(0, 3))]
    for i, _ in enumerate(mounts):
        total = 100_000_000 * (i + 1)
        used_kb = int(total * (0.4 + i * 0.25) + row * 37)
        out += ["xfs", str(total), str(used_kb), str(total - used_kb), str(round(used_kb / total * 100))]
    return out


def main():
    rng = random.Random(20261106)
    header = ["timestamp"]
    for s in SERVERS:
        header += columns(s[0], s[1], s[2], s[3], s[4])

    lines = [",".join(header)]
    for row in range(ROWS):
        stamp = START + timedelta(milliseconds=row * 1000 + rng.randint(0, 40))
        fields = [stamp.strftime("%Y%m%d-%H%M%S.") + f"{stamp.microsecond // 1000:03d}"]
        for s in SERVERS:
            values = cells(rng, row, *s)
            if s[0] == "db01" and row in OUTAGE:
                values = ["Error"] + [""] * (len(values) - 1)
            fields += values
        lines.append(",".join(fields))

    out = Path(__file__).with_name("combined-20261106-141500.csv")
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{out.name}: {len(header)} columns, {ROWS} rows")


if __name__ == "__main__":
    main()
