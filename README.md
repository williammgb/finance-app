# finance-app

A Blazor Server app (.NET 10) for one person on one PC: each month you enter what you
spent per category, and the overview page shows it. It runs on localhost over plain
http, with no login. Your own spending data stays in `data/`, which git ignores, and
the fast gate refuses any commit that tracks a database or `data/` file.

## Install

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). From a clone of this repository:

```sh
dotnet pack src/FinanceApp -c Release
dotnet tool install -g FinanceApp --add-source ./nupkg
```

## Use it

```sh
financeapp        # serve the app at http://localhost:5080
financeapp demo   # serve it at http://localhost:5081 on example data
```

Your data is kept in `%LOCALAPPDATA%\FinanceApp` (on Linux and macOS, `~/.local/share/FinanceApp`),
so it survives updating or reinstalling the app. To update after pulling new code, pack again and
run `dotnet tool update -g FinanceApp --add-source ./nupkg`.

## Develop

Needs Git Bash as well. Run from source, the app keeps its data in `src/FinanceApp/data/`.

```sh
./run setup   # restore the local tools and the locked NuGet packages
./run start   # serve the app at http://localhost:5080
./run demo    # serve the app at http://localhost:5081 on example data
```

## Screenshot

The overview page on the demo data:

![The overview page: the latest month's total, the change against the month before and the 12-month average, a line chart of spending per month and a donut chart of the latest month](docs/overview.png)
