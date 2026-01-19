// Carousel navigation - scroll to next/prev slide
document.addEventListener('click', (e) => {
    // Arrow button navigation
    const navBtn = e.target.closest('.carousel .nav');
    if (navBtn) {
        const slides = navBtn.closest('.slides');
        const dir = parseInt(navBtn.dataset.dir, 10);
        const slideWidth = slides.querySelector('.slide').offsetWidth;
        slides.scrollBy({ left: dir * slideWidth, behavior: 'smooth' });
        return;
    }
    
    // Dot button navigation
    const dotBtn = e.target.closest('.carousel .dot');
    if (dotBtn) {
        const carousel = dotBtn.closest('.carousel');
        const slides = carousel.querySelector('.slides');
        const index = parseInt(dotBtn.dataset.index, 10);
        const slideWidth = slides.querySelector('.slide').offsetWidth;
        slides.scrollTo({ left: index * slideWidth, behavior: 'smooth' });
    }
});

// Update active dot on scroll using IntersectionObserver
document.querySelectorAll('.carousel').forEach(carousel => {
    const slides = carousel.querySelector('.slides');
    const dots = carousel.querySelectorAll('.dot');
    if (!slides || dots.length === 0) return;
    
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const index = [...slides.children].indexOf(entry.target);
                dots.forEach((dot, i) => dot.classList.toggle('active', i === index));
            }
        });
    }, { root: slides, threshold: 0.5 });
    
    slides.querySelectorAll('.slide').forEach(slide => observer.observe(slide));
});
