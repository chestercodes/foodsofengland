document.addEventListener("DOMContentLoaded", () => {
  const dataEl = document.getElementById("ingredients-data");
  if (!dataEl) return;
  const dishes = JSON.parse(dataEl.textContent);
  
  const toggles = Array.from(document.querySelectorAll('.ingredient-toggle input'));
  const results = document.getElementById('ingredient-results');
  const summary = document.getElementById('ingredient-summary');
  if (!results || !summary) return;

  function renderResults() {
    const selected = toggles.filter(toggle => toggle.checked).map(toggle => toggle.value);
    const filtered = selected.length === 0
      ? dishes
      : dishes.filter(dish => selected.every(ingredient => dish.ingredients.includes(ingredient)));
    const summaryText = selected.length === 0
      ? `${dishes.length} dishes available. Select one or more ingredients to narrow the list.`
      : `${filtered.length} dishes match ${selected.join(', ')}.`;
    summary.textContent = summaryText;
    results.innerHTML = filtered.map(dish => {
      return `<li class="result-item"><a href="${dish.href}">${dish.title}</a><p class="result-item__meta">${dish.firstLine || ''}</p></li>`;
    }).join('');
    if (filtered.length === 0) {
      results.innerHTML = '<li class="result-item">No dishes matched that ingredient combination.</li>';
    }
  }
  toggles.forEach(toggle => toggle.addEventListener('change', renderResults));
  renderResults();

  const searchInput = document.getElementById("ingredient-search");
  if (searchInput) {
    const togglesList = Array.from(document.querySelectorAll('.ingredient-toggle'));
    searchInput.addEventListener("input", () => {
      const query = searchInput.value.trim().toLowerCase();
      togglesList.forEach(toggle => {
        const text = toggle.querySelector('span').textContent.toLowerCase();
        if (text.includes(query)) {
          toggle.style.display = "flex";
        } else {
          toggle.style.display = "none";
        }
      });
    });
  }
});
