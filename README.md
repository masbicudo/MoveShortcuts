# Move Shortcuts Project

Helps in organizing the link mess that Choco makes on your Desktop... and more!

## Introduction

When using Chocolatey to install or update applications my desktop often
gets cluttered with program links. What I want, is to free myself the work
to clean up the mess, but also I don't want to lose the links...
what to do then?

What this program does is to create a new folder at `C:\Shortcuts` where
it places every shortcut it finds as being clutter to the Desktop.
This became so useful because then I added this folder to the `PATH`
environment variable, and made lists of small commands to associate with
each one of these programs.

For example, the link `"Visual Studio Code"` will be sent to the special
shortcuts folder when found on the Desktop, but also new links will be created:
`"code"` and `"coda"`. The first open VS Code normally,
the second opens it with elevated privileges.
