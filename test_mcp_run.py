import subprocess
import json
import time
import threading

def read_stdout(proc):
    for line in proc.stdout:
        print(f"[STDOUT] {line.strip()}")

def read_stderr(proc):
    for line in proc.stderr:
        pass # Ignore stderr for cleaner output

proc = subprocess.Popen(["jaravi-mcp", "--stdio"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")

t1 = threading.Thread(target=read_stdout, args=(proc,))
t1.daemon = True
t1.start()
t2 = threading.Thread(target=read_stderr, args=(proc,))
t2.daemon = True
t2.start()

def send_request(method, params=None, id=1):
    req = {
        "jsonrpc": "2.0",
        "id": id,
        "method": method
    }
    if params is not None:
        req["params"] = params
    msg = json.dumps(req)
    print(f"-> {msg}")
    proc.stdin.write(msg + "\n")
    proc.stdin.flush()

time.sleep(1)
send_request("initialize", {
    "protocolVersion": "2024-11-05",
    "capabilities": {},
    "clientInfo": {"name": "test-client", "version": "1.0.0"}
}, id=1)
time.sleep(1)
send_request("notifications/initialized")
time.sleep(1)

send_request("tools/call", {
    "name": "run_agent",
    "arguments": {
        "profile": "opencode",
        "workdir": "C:\\Users\\USER\\source\\APPS-C++\\consola\\jaravi",
        "task": "Responde con un 'Hola Jaravi, soy OpenCode y funciono correctamente.'"
    }
}, id=2)

time.sleep(15)
proc.kill()
