#!/usr/bin/env bash
#
# Creates the Brainy 7.0 epic and its sub-issues on GitHub, then links each
# sub-issue to the epic via the GraphQL sub-issue API.
#
# Requires: gh CLI, authenticated with repo write access.
#   gh auth status
#
# Usage:
#   ./create-issues.sh              # create everything
#   ./create-issues.sh --dry-run    # print what would be created
#
set -euo pipefail

REPO="${REPO:-kasuken/Brainy}"
DRY_RUN=0
[[ "${1:-}" == "--dry-run" ]] && DRY_RUN=1

cd "$(dirname "$0")"

# Reads "# Title" from line 1 of a file.
title_of() { head -n 1 "$1" | sed 's/^# *//'; }

# Reads the "<!-- labels: a,b,c -->" marker into the global LABEL_ARGS array.
labels_of() {
  LABEL_ARGS=()
  local raw
  raw="$(sed -n 's/^<!-- labels: *\(.*\) *-->$/\1/p' "$1" | head -n 1)"
  [[ -z "$raw" ]] && return 0
  local IFS=','
  for label in $raw; do
    label="$(echo "$label" | sed 's/^ *//; s/ *$//')"
    [[ -n "$label" ]] && LABEL_ARGS+=(--label "$label")
  done
}

# Strips the title line and the labels marker, leaving the issue body.
body_of() { tail -n +3 "$1" | sed '/./,$!d'; }

create_issue() {
  local file="$1"
  local title body tmp
  title="$(title_of "$file")"
  labels_of "$file"
  body="$(body_of "$file")"

  if [[ $DRY_RUN -eq 1 ]]; then
    echo "would create: $title  [${LABEL_ARGS[*]:-no labels}]" >&2
    echo "https://github.com/$REPO/issues/0"
    return 0
  fi

  tmp="$(mktemp)"
  printf '%s\n' "$body" > "$tmp"
  gh issue create --repo "$REPO" --title "$title" --body-file "$tmp" "${LABEL_ARGS[@]}"
  rm -f "$tmp"
}

# Resolves an issue number to its GraphQL node ID.
node_id_of() { gh issue view "$1" --repo "$REPO" --json id --jq .id; }

link_sub_issue() {
  local parent_id="$1" child_id="$2"
  gh api graphql \
    -H "GraphQL-Features: sub_issues" \
    -f query='mutation($parent:ID!,$child:ID!){addSubIssue(input:{issueId:$parent,subIssueId:$child}){clientMutationId}}' \
    -f parent="$parent_id" -f child="$child_id" >/dev/null
}

echo "==> Creating epic"
EPIC_URL="$(create_issue 00-epic.md)"
EPIC_NUM="${EPIC_URL##*/}"
echo "    epic: $EPIC_URL"

if [[ $DRY_RUN -eq 0 ]]; then
  EPIC_ID="$(node_id_of "$EPIC_NUM")"
fi

echo "==> Creating sub-issues"
for file in [0-9][0-9]-*.md; do
  [[ "$file" == "00-epic.md" ]] && continue
  url="$(create_issue "$file")"
  num="${url##*/}"
  echo "    #$num  $(title_of "$file")"
  if [[ $DRY_RUN -eq 0 ]]; then
    link_sub_issue "$EPIC_ID" "$(node_id_of "$num")"
    sleep 1   # be gentle with the API
  fi
done

echo
echo "Done. Epic: $EPIC_URL"
echo
echo "Note: #300 (hybrid search) and #303 (document OCR) already exist and stay"
echo "parented under #292. Attach them to the new epic manually if you prefer:"
echo "  GitHub UI -> issue -> Relationships -> Parent issue"
