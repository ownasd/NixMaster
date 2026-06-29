#!/bin/bash
set -e 

# Initialize if not already a git repo
if [ ! -d ".git" ]; then
    git init
fi

# Automatically set local identity for this repo only
git config user.email "ownpriyanshu@gmail.com"
git config user.name "ownpriyanshu-spec"

git add .
git commit -m "UI Updates: Search Unit ID, Plan/Target in Settings, SubAssy Box Colors"

# Set or update the remote URL seamlessly
git remote set-url origin https://github.com/ownpriyanshu-spec/nixmaster.git 2>/dev/null || git remote add origin https://github.com/ownpriyanshu-spec/nixmaster.git

git branch -M master
git push -u origin master --force

echo "✅ Code push complete!"
