# Adiciona a src/HWSuite.Core/Benchmark/Data/cpu-reference.csv os resultados enviados pelo botão
# "Enviar resultado" (issues "[benchmark] ..." do GitHub). Revise cada issue antes de rodar.
# Uso: python3 tools/import-benchmark-issues.py [--dry-run]
# Precisa do GitHub CLI (gh) autenticado. Linhas já presentes não são duplicadas.
import json, re, subprocess, sys

REFERENCE = "src/HWSuite.Core/Benchmark/Data/cpu-reference.csv"
issues = json.loads(subprocess.check_output([
    "gh", "issue", "list", "--repo", "aprodrigues31/HWSuite", "--state", "all", "--limit", "500",
    "--search", "[benchmark] in:title", "--json", "number,title,body"]))
existing = set(open(REFERENCE, encoding="utf-8").read().splitlines())
new = []
for issue in issues:
    match = re.search(r"```csv\s*\n(.+?)\n\s*```", issue.get("body") or "", re.S)
    line = match.group(1).strip() if match else None
    if not line or line.count(",") < 5 or line in existing:
        continue
    print(f"#{issue['number']}: {line}")
    new.append(line)
if new and "--dry-run" not in sys.argv:
    with open(REFERENCE, "a", encoding="utf-8") as f:
        f.write("\n".join(new) + "\n")
print(f"{len(new)} new result(s)")
