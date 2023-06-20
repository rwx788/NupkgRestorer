#!/usr/bin/env bash

version=0.0.2
declare -a runtimes=("osx-x64" "osx.11.0-arm64" "linux-x64" "linux-arm64" "win-x64" "win-arm64")

for runtime in "${runtimes[@]}"
do
   dotnet publish -c Release --self-contained --runtime $runtime
done

BASEDIR=$(dirname "$0")
mkdir -p $BASEDIR/release
rm -rf $BASEDIR/release/*

for runtime in "${runtimes[@]}"
do
    if [ $runtime == "osx.11.0-arm64" ]; then
        releasedir="osx-arm64"
    else
        releasedir=$runtime
    fi
    mkdir -p release/$releasedir
    publishdir=$BASEDIR/NupkgRestorer/bin/Release/net*.0/$runtime/publish
    if [ -e $publishdir/NupkgRestorer.exe ]; then
        cp $publishdir/NupkgRestorer.exe $BASEDIR/release/$releasedir/
    else
        cp $publishdir/NupkgRestorer $BASEDIR/release/$releasedir/
    fi
    
    zip -r -j $BASEDIR/release/NupkgRestorer-$version-$releasedir.zip $BASEDIR/release/$releasedir/*
done
