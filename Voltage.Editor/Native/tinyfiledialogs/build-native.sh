#!/usr/bin/env bash
# Builds the tinyfiledialogs shared library for the current host OS into runtimes/<rid>/native/.
# macOS produces a universal (x86_64 + arm64) dylib shared by both osx RIDs.
set -euo pipefail
cd "$(dirname "$0")"

uname_s="$(uname -s)"
case "$uname_s" in
	Darwin)
		echo "Building macOS universal dylib..."
		mkdir -p runtimes/osx-x64/native runtimes/osx-arm64/native
		clang -arch x86_64 -arch arm64 -dynamiclib -O2 -fPIC \
			-framework AppKit -o /tmp/libtinyfiledialogs.dylib tinyfiledialogs.c
		cp /tmp/libtinyfiledialogs.dylib runtimes/osx-x64/native/
		cp /tmp/libtinyfiledialogs.dylib runtimes/osx-arm64/native/
		rm -f /tmp/libtinyfiledialogs.dylib
		echo "Done: runtimes/osx-{x64,arm64}/native/libtinyfiledialogs.dylib"
		;;
	Linux)
		echo "Building Linux shared object..."
		mkdir -p runtimes/linux-x64/native
		cc -shared -O2 -fPIC -o runtimes/linux-x64/native/libtinyfiledialogs.so tinyfiledialogs.c
		echo "Done: runtimes/linux-x64/native/libtinyfiledialogs.so"
		;;
	*)
		echo "Unsupported host '$uname_s'. On Windows, run build-native.cmd instead." >&2
		exit 1
		;;
esac
