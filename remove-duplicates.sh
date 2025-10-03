#!/bin/bash

# Get all project entries with their line numbers, names, and paths
grep -n "Project(" Koan.sln | while IFS=: read -r linenum rest; do
    project_name=$(echo "$rest" | sed 's/.*= "\([^"]*\)".*/\1/')
    project_path=$(echo "$rest" | sed 's/.*", "\([^"]*\)".*/\1/')
    echo "$linenum|$project_name|$project_path"
done | sort -t'|' -k2,2 -k1,1n | awk -F'|' '
{
    key = $2 "|" $3
    if (seen[key]) {
        print "Duplicate at line", $1, ":", $2, $3
        dup_lines[++dup_count] = $1
    } else {
        seen[key] = 1
    }
}
END {
    for (i = dup_count; i >= 1; i--) {
        print dup_lines[i]
    }
}' | tail -n +$(grep -c "^Duplicate" /dev/stdin || echo 0)
