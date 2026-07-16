#!/bin/bash
echo "Starting update..."
mkdir -p TmsApi.Api/Controllers
mv Controllers/* TmsApi.Api/Controllers/
mkdir -p TmsApi.Api/Middlewares
mkdir -p TmsApi.Api/Options
mkdir -p TmsApi.Api/Properties
mv Properties/* TmsApi.Api/Properties/
mv Program.cs TmsApi.Api/ 2>/dev/null || true
mv appsettings.json TmsApi.Api/ 2>/dev/null || true
mv appsettings.Development.json TmsApi.Api/ 2>/dev/null || true
echo "System updated successfully."