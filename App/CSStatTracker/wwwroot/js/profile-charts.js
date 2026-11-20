// Draws the three monthly trend charts and the Premier rating chart on the
// Player Profile page.
// Each metric gets its own small chart (one series, one axis) instead of
// sharing a dual-axis chart. The table under the charts has the same numbers.
(function () {
    if (typeof Chart === "undefined") return;

    const read = (id) => {
        const el = document.getElementById(id);
        return el ? JSON.parse(el.textContent) : null;
    };
    const trend = read("trend-data");
    const premier = read("premier-data");
    const css = getComputedStyle(document.documentElement);
    const token = (name) => css.getPropertyValue(name).trim();
    const accent = token("--accent");
    const muted = token("--muted");
    const grid = token("--line");
    const surface = token("--surface");

    Chart.defaults.font.family = token("--body") || "system-ui";
    Chart.defaults.color = muted;

    // labels: x-axis labels; tip(i): extra tooltip text for point i.
    function lineChart(canvasId, labels, values, { format, min, max, tip }) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const lastIndex = labels.length - 1;

        new Chart(canvas, {
            type: "line",
            data: {
                labels,
                datasets: [{
                    data: values,
                    borderColor: accent,
                    backgroundColor: accent,
                    borderWidth: 2,
                    borderJoinStyle: "round",
                    borderCapStyle: "round",
                    tension: 0.25,
                    spanGaps: true,
                    // Only the latest month gets a dot; hovering shows the rest.
                    pointRadius: (ctx) => (ctx.dataIndex === lastIndex ? 4 : 0),
                    pointHoverRadius: 5,
                    pointBorderColor: surface,
                    pointBorderWidth: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? false : { duration: 300 },
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            label: (ctx) => format(ctx.parsed.y) + (tip ? tip(ctx.dataIndex) : "")
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { color: grid },
                        ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 6 }
                    },
                    y: {
                        min, max,
                        grid: { color: grid, lineWidth: 1 },
                        border: { display: false },
                        ticks: { maxTicksLimit: 5, callback: (v) => format(v) }
                    }
                }
            }
        });
    }

    if (trend) {
        const games = (i) => ` · ${trend.games[i]} matches`;
        lineChart("chart-kd", trend.labels, trend.kd, { format: (v) => Number(v).toFixed(2), tip: games });
        lineChart("chart-adr", trend.labels, trend.adr, { format: (v) => Number(v).toFixed(0), tip: games });
        lineChart("chart-win", trend.labels, trend.winRate, { format: (v) => `${Number(v).toFixed(0)}%`, min: 0, max: 100, tip: games });
    }

    if (premier) {
        lineChart("chart-premier", premier.labels, premier.rating, {
            format: (v) => Math.round(Number(v)).toLocaleString("en-US")
        });
    }
})();
