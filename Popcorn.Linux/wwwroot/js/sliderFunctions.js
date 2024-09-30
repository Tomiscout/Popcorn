window.getItemsPerScreen = (id) => {
    return getComputedStyle(document.getElementById(id)).getPropertyValue("--items-per-screen")
}

window.setSliderIndex = (id, index) => {
    document.getElementById(id).style.setProperty("--slider-index", index);
}
