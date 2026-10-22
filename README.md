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

## Demo

```sh
./run demo    # serve the app at http://localhost:5081 on example data
```

The demo shows two years of spending (November 2024 to October 2026) in 11 categories, for
screenshots. The data is made up: it comes from `samples/example-spending.csv`, which is committed.
The demo runs on its own database, `data/demo.db`, rebuilt from that file at every start, so changes
made in the demo are gone at the next one and your own `finance.db` is never opened. It runs on its
own port, beside `./run start`; its browser tab reads "Finance (demo)".

## Screenshot

The overview page on the demo data:

![The overview page: the latest month's total, the change against the month before and the 12-month average, a line chart of spending per month and a donut chart of the latest month](docs/overview.png)

## Work on it

```sh
./run fast    # data-file guard, build with warnings as errors, tests
./run full    # fast, then a format check and a smoke run of the real app
```
