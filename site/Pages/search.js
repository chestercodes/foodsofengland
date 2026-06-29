document.addEventListener("DOMContentLoaded", () => {
  const bgImages = [
    "/assets/backgrounds/cotswolds.jpg",
    "/assets/backgrounds/towerbridge.jpg",
    "/assets/backgrounds/stonehenge.jpg",
    "/assets/backgrounds/lakedistrict.jpg",
    "/assets/backgrounds/castlecombe.jpg",
    "/assets/backgrounds/hadrians_wall.jpg",
    "/assets/backgrounds/windsor_castle.jpg",
    "/assets/backgrounds/roman_baths.jpg",
    "/assets/backgrounds/durham_cathedral.jpg",
    "/assets/backgrounds/york_minster.jpg",
    "/assets/backgrounds/oxford_college.jpg",
    "/assets/backgrounds/cambridge_river.jpg"
  ];

  const selectedBg = bgImages[Math.floor(Math.random() * bgImages.length)];
  document.body.style.backgroundImage = `linear-gradient(180deg, rgba(255,255,255,0.78) 0%, rgba(255,255,255,0.88) 100%), url('${selectedBg}')`;

  const input = document.getElementById("search-input");
  const results = document.getElementById("search-results");
  if (!input || !results) return;

  document.addEventListener("click", (e) => {
    if (!input.contains(e.target) && !results.contains(e.target)) {
      results.style.display = "none";
    }
  });

  input.addEventListener("focus", () => {
    if (results.children.length > 0) {
      results.style.display = "block";
    }
  });

  let debounceTimeout;
  input.addEventListener("input", () => {
    clearTimeout(debounceTimeout);
    const query = input.value.trim();
    if (query.length < 2) {
      results.innerHTML = "";
      results.style.display = "none";
      return;
    }

    debounceTimeout = setTimeout(() => {
      fetch(`/api/search?q=${encodeURIComponent(query)}`)
        .then(res => res.json())
        .then(data => {
          results.innerHTML = "";
          if (data.length === 0) {
            results.innerHTML = "<li class='search-widget__result-item search-widget__no-results'>No results found</li>";
            results.style.display = "block";
            return;
          }
          data.forEach(item => {
            const li = document.createElement("li");
            li.className = "search-widget__result-item";
            const a = document.createElement("a");
            a.href = "/" + item.finalPath;
            a.textContent = item.title;
            li.appendChild(a);
            results.appendChild(li);
          });
          results.style.display = "block";
        })
        .catch(err => console.error("Search error:", err));
    }, 250);
  });
});
