# finance-app

A personal finance web app in .NET: monthly spending per category, shown as charts.

A Blazor Server app (.NET 10) for one person on one PC: each month you enter what you
spent per category, and the overview page shows it. It runs on localhost over plain
http, with no login. Your own spending data stays in `data/`, which git ignores, and
the fast gate refuses any commit that tracks a database or `data/` file.

## Use it

Needs the .NET 10 SDK and Git Bash.

```sh
./run setup   # restore the local tools and the locked NuGet packages
./run start   # serve the app at http://localhost:5080
```

## Work on it

```sh
./run fast    # data-file guard, build with warnings as errors, tests
./run full    # fast, then a format check and a smoke run of the real app
```
