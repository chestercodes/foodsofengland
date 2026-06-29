document.addEventListener('DOMContentLoaded', function() {
    var dataElement = document.getElementById('categories-data');
    if (!dataElement) return;
    var posts = JSON.parse(dataElement.textContent);
    
    var placeholder = document.getElementById('category-placeholder');
    var selectedTitle = document.getElementById('category-selected-title');
    var dishesList = document.getElementById('category-dishes-list');
    
    var pills = document.querySelectorAll('.tag-pill[data-filter-type]');
    pills.forEach(function(pill) {
        pill.addEventListener('click', function(e) {
            e.preventDefault();
            
            pills.forEach(function(p) { p.classList.remove('active'); });
            pill.classList.add('active');
            
            var filterType = pill.getAttribute('data-filter-type');
            var filterValue = pill.getAttribute('data-filter-value');
            var friendlyName = pill.getAttribute('data-friendly-name');
            
            var filtered = posts.filter(function(post) {
                if (filterType === 'class') {
                    return post.c && post.c.includes(filterValue);
                } else if (filterType === 'status') {
                    return post.s && post.s.includes(filterValue);
                } else if (filterType === 'locale') {
                    return post.l && post.l.includes(filterValue);
                }
                return false;
            });
            
            placeholder.style.display = 'none';
            selectedTitle.style.display = 'block';
            selectedTitle.textContent = friendlyName + ' (' + filtered.length + ')';
            
            dishesList.innerHTML = '';
            filtered.forEach(function(post) {
                var li = document.createElement('li');
                li.className = 'result-item';
                
                var a = document.createElement('a');
                a.href = post.u;
                a.textContent = post.t;
                li.appendChild(a);

                if (post.d) {
                    var p = document.createElement('p');
                    p.className = 'result-item__meta';
                    p.textContent = post.d;
                    li.appendChild(p);
                }
                
                dishesList.appendChild(li);
            });
        });
    });

    var searchInput = document.getElementById('category-search');
    if (searchInput) {
        var categoryItems = Array.from(document.querySelectorAll('.tag-list li'));
        searchInput.addEventListener('input', function() {
            var query = searchInput.value.trim().toLowerCase();
            categoryItems.forEach(function(item) {
                var pill = item.querySelector('.tag-pill');
                var text = pill ? pill.getAttribute('data-friendly-name').toLowerCase() : '';
                if (text.includes(query)) {
                    item.style.display = '';
                } else {
                    item.style.display = 'none';
                }
            });
        });
    }
});
