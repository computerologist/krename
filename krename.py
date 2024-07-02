import sys, os, re, json, logging, subprocess, copy
from datetime import datetime
from PyQt5.QtWidgets import (
    QApplication, QWidget, QVBoxLayout, QHBoxLayout, QLineEdit, QPushButton,
    QTreeWidget, QTreeWidgetItem, QCheckBox, QFileDialog, QLabel, QComboBox, QMessageBox, QMenu,
    QTabWidget, QSpinBox, QPlainTextEdit, QAbstractItemView, QCompleter,  QDialog, QVBoxLayout
)
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QColor
import concurrent.futures
from tree_data import TreeData

SETTINGS_FILE = "krename_settings.json"

class Krenamer(QWidget):
    def __init__(self):
        super().__init__()
        self.settings = self.load_settings()
        self.file_data = None
        self.mirror_data = None

        logging_level = getattr(logging, self.settings.get("logging_level", "DEBUG"))
        debug_logfile_location = self.settings["debug_logfile_location"]
        debug_log_file = os.path.join(debug_logfile_location, 'KRENAMER.log')
        open(debug_log_file, 'w').close()
        logging.basicConfig(filename=debug_log_file, level=logging_level, format='%(asctime)s - %(levelname)s - %(message)s')

        self.initUI()
        self.setup_connections()

    def load_settings(self):
        if os.path.exists(SETTINGS_FILE):
            with open(SETTINGS_FILE, 'r', encoding='utf-8') as f:
                settings = json.load(f)
        else:
            settings = {}

        settings.setdefault("path_history", [])
        settings.setdefault("match_history", [])
        settings.setdefault("extension_history", [])
        settings.setdefault("search_history", [])
        settings.setdefault("replace_history", [])
        settings.setdefault("add_text_history", [])
        settings.setdefault("stop_on_error", False)
        settings.setdefault("include_logfile", False)
        settings.setdefault("logfile_location", os.path.dirname(os.path.abspath(__file__)))
        settings.setdefault("debug_logfile_location", os.path.dirname(os.path.abspath(__file__)))
        settings.setdefault("add_text", False)
        settings.setdefault("add_text_value", "")
        settings.setdefault("search_replace_checked", False)
        settings.setdefault("include_subfolders_checked", False)
        settings.setdefault("match_case_sensitive_checked", False)

        settings.setdefault("case_sensitive_checked", False)
        settings.setdefault("add_text_checked", False)
        settings.setdefault("logging_level", "CRITICAL")  # Add logging level setting

        return settings

    def save_settings(self):
        with open(SETTINGS_FILE, 'w') as f:
            json.dump(self.settings, f, indent=4)

    def create_case_sensitive_completer(self,items):
        completer = QCompleter(items)
        completer.setCaseSensitivity(Qt.CaseSensitive)
        return completer

    def initUI(self):
        self.tabs = QTabWidget()
        self.main_tab = QWidget()
        self.config_tab = QWidget()

        self.init_main_tab()
        self.init_config_tab()

        self.tabs.addTab(self.main_tab, "Main")
        self.tabs.addTab(self.config_tab, "Config")

        main_layout = QVBoxLayout()
        main_layout.addWidget(self.tabs)
        self.setLayout(main_layout)
        self.setWindowTitle('krename')
        self.setGeometry(300, 300, 800, 600)

    def init_main_tab(self):
        layout = QVBoxLayout(self.main_tab)

        # Path browsing widget
        path_layout = QHBoxLayout()
        path_label = QLabel("File Path:")
        path_layout.addWidget(path_label)
        self.path_combo = QComboBox()
        self.path_combo.setEditable(True)
        if self.settings["path_history"]:
            self.path_combo.addItems(self.settings["path_history"])
        path_layout.addWidget(self.path_combo)
        self.browse_button = QPushButton('Browse')
        path_layout.addWidget(self.browse_button)
        self.load_button = QPushButton('Load')
        path_layout.addWidget(self.load_button)
        layout.addLayout(path_layout)

        # Matching string textbox
        match_layout = QHBoxLayout()
        match_label = QLabel("Match:")
        match_layout.addWidget(match_label)

        self.match_combo = QComboBox()
        self.match_combo.setEditable(True)
        if self.settings["match_history"]:
            self.match_combo.addItems(self.settings["match_history"])
        self.match_combo.setCompleter(self.create_case_sensitive_completer(self.settings["match_history"]))
        self.match_combo.setCurrentText("")
        match_layout.addWidget(self.match_combo)
        self.clear_match_button = QPushButton('Clear')
        match_layout.addWidget(self.clear_match_button)

        self.match_case_sensitive_checkbox = QCheckBox('Case Sensitive')
        self.match_case_sensitive_checkbox.setChecked(self.settings["match_case_sensitive_checked"])
        match_layout.addWidget(self.match_case_sensitive_checkbox)

        layout.addLayout(match_layout)

        # Extension filter textbox
        extension_layout = QHBoxLayout()
        extension_label = QLabel("Extension:")
        extension_layout.addWidget(extension_label)
        self.extension_combo = QComboBox()
        self.extension_combo.setEditable(True)
        if self.settings["extension_history"]:
            self.extension_combo.addItems(self.settings["extension_history"])
        #self.extension_combo.setCurrentText("")
        extension_layout.addWidget(self.extension_combo)
        self.clear_extension_button = QPushButton('Clear')
        extension_layout.addWidget(self.clear_extension_button)
        layout.addLayout(extension_layout)  

        # Horizontal layout for file trees
        trees_layout = QHBoxLayout()

        # File tree widget
        file_tree_layout = QVBoxLayout()
        file_tree_label = QLabel("Files:")
        file_tree_layout.addWidget(file_tree_label)
        self.file_tree = QTreeWidget()
        self.file_tree.setHeaderLabels([''])
        self.file_tree.setSelectionMode(QAbstractItemView.ExtendedSelection)
        file_tree_layout.addWidget(self.file_tree)
        
        # Checkbox for including subfolders
        self.include_subfolders_checkbox = QCheckBox('Include Subfolders')
        self.include_subfolders_checkbox.setChecked(self.settings["include_subfolders_checked"])
        file_tree_layout.addWidget(self.include_subfolders_checkbox)
        trees_layout.addLayout(file_tree_layout)

        # Mirror file tree widget
        mirror_layout = QVBoxLayout()
        mirror_label = QLabel("Files to be Renamed:")
        mirror_layout.addWidget(mirror_label)
        self.mirror_tree = QTreeWidget()
        self.mirror_tree.setHeaderLabels([''])
        mirror_layout.addWidget(self.mirror_tree)
        trees_layout.addLayout(mirror_layout)

        # Checkbox for case sensitive
        self.case_sensitive_checkbox = QCheckBox('Case Sensitive')
        self.case_sensitive_checkbox.setChecked(self.settings["case_sensitive_checked"])
        mirror_layout.addWidget(self.case_sensitive_checkbox)

        layout.addLayout(trees_layout)

        # Search and replace text entry boxes
        search_replace_layout = QHBoxLayout()
        
        self.search_replace_checkbox = QCheckBox()
        self.search_replace_checkbox.setChecked(self.settings["search_replace_checked"])
        search_replace_layout.addWidget(self.search_replace_checkbox)
        
        search_label = QLabel("Search:")
        search_replace_layout.addWidget(search_label)
        self.search_combo = QComboBox()
        self.search_combo.setEditable(True)
        if self.settings["search_history"]:
            self.search_combo.addItems(self.settings["search_history"])
        self.search_combo.setCompleter(self.create_case_sensitive_completer(self.settings["search_history"]))
        search_replace_layout.addWidget(self.search_combo)
        
        replace_label = QLabel("Replace:")
        search_replace_layout.addWidget(replace_label)
        self.replace_combo = QComboBox()
        self.replace_combo.setEditable(True)
        if self.settings["replace_history"]:
            self.replace_combo.addItems(self.settings["replace_history"])
        self.replace_combo.setCompleter(self.create_case_sensitive_completer(self.settings["replace_history"]))
        search_replace_layout.addWidget(self.replace_combo)
        
        layout.addLayout(search_replace_layout)

        # Add text entry box
        add_text_layout = QHBoxLayout()
        
        self.add_text_checkbox = QCheckBox()
        self.add_text_checkbox.setChecked(self.settings["add_text_checked"])
        add_text_layout.addWidget(self.add_text_checkbox)
        add_text_label = QLabel("Add text:")
        add_text_layout.addWidget(add_text_label)
        self.add_text_combo = QComboBox()
        self.add_text_combo.setEditable(True)
        if self.settings["add_text_history"]:
            self.add_text_combo.addItems(self.settings["add_text_history"])
        self.add_text_combo.setCompleter(self.create_case_sensitive_completer(self.settings["add_text_history"]))
        add_text_layout.addWidget(self.add_text_combo)
        
        add_text_position_label = QLabel("at position:")
        add_text_layout.addWidget(add_text_position_label)
        self.add_text_position_spinbox = QSpinBox()
        self.add_text_position_spinbox.setRange(-1000, 1000)
        self.add_text_position_spinbox.setValue(self.settings.get("add_text_position", 0))
        add_text_layout.addWidget(self.add_text_position_spinbox)
        
        layout.addLayout(add_text_layout)

        # Rename button
        rename_layout = QHBoxLayout()
        self.rename_button = QPushButton('Rename')
        self.rename_button.setEnabled(False)
        rename_layout.addWidget(self.rename_button)
        layout.addLayout(rename_layout)

        # disable autocomplete
        self.path_combo.setInsertPolicy(QComboBox.NoInsert)
        self.match_combo.setInsertPolicy(QComboBox.NoInsert)
        self.extension_combo.setInsertPolicy(QComboBox.NoInsert)
        self.search_combo.setInsertPolicy(QComboBox.NoInsert)
        self.replace_combo.setInsertPolicy(QComboBox.NoInsert)
        self.add_text_combo.setInsertPolicy(QComboBox.NoInsert)

    def init_config_tab(self):
        layout = QVBoxLayout(self.config_tab)

        self.stop_on_error_checkbox = QCheckBox('Stop on Error')
        self.stop_on_error_checkbox.setChecked(self.settings.get("stop_on_error", False))
        layout.addWidget(self.stop_on_error_checkbox)

        self.include_logfile_checkbox = QCheckBox('Include Logfile')
        self.include_logfile_checkbox.setChecked(self.settings.get("include_logfile", False))
        layout.addWidget(self.include_logfile_checkbox)

        logfile_layout = QHBoxLayout()
        logfile_label = QLabel("Logfile Location:")
        logfile_layout.addWidget(logfile_label)
        self.logfile_location_edit = QLineEdit(self.settings.get("logfile_location", ""))
        logfile_layout.addWidget(self.logfile_location_edit)
        self.logfile_browse_button = QPushButton("Browse")
        logfile_layout.addWidget(self.logfile_browse_button)
        layout.addLayout(logfile_layout)

        debug_logfile_layout = QHBoxLayout()
        debug_logfile_label = QLabel("Debug logfile Location:")
        debug_logfile_layout.addWidget(debug_logfile_label)
        self.debug_logfile_location_edit = QLineEdit(self.settings.get("debug_logfile_location", ""))
        debug_logfile_layout.addWidget(self.debug_logfile_location_edit)
        self.debug_logfile_browse_button = QPushButton("Browse")
        debug_logfile_layout.addWidget(self.debug_logfile_browse_button)
        layout.addLayout(debug_logfile_layout)

        logging_level_layout = QHBoxLayout()
        logging_level_label = QLabel("Logging Level:")
        logging_level_layout.addWidget(logging_level_label)
        self.logging_level_combo = QComboBox()
        self.logging_level_combo.addItems(["DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"])
        self.logging_level_combo.setCurrentText(self.settings.get("logging_level", "DEBUG"))
        logging_level_layout.addWidget(self.logging_level_combo)
        layout.addLayout(logging_level_layout)

    def sync_scroll(self, value):
        # Block signals temporarily to avoid recursive updates
        self.file_tree.verticalScrollBar().blockSignals(True)
        self.mirror_tree.verticalScrollBar().blockSignals(True)

        # Update the scroll positions
        self.file_tree.verticalScrollBar().setValue(value)
        self.mirror_tree.verticalScrollBar().setValue(value)

        # Unblock signals
        self.file_tree.verticalScrollBar().blockSignals(False)
        self.mirror_tree.verticalScrollBar().blockSignals(False)

    def setup_connections(self):
        self.browse_button.clicked.connect(self.browse_directory)
        self.load_button.clicked.connect(self.update_file_data)
        self.path_combo.activated.connect(
            lambda: self.on_combobox_activated(self.path_combo, "path_history"))
        self.match_combo.activated.connect(
            lambda: self.on_combobox_activated(self.match_combo, "match_history"))
        self.extension_combo.activated.connect(
            lambda: self.on_combobox_activated(self.extension_combo, "extension_history"))
        self.search_combo.lineEdit().editingFinished.connect(
            lambda: self.textfield_update_save(self.search_combo, "search_history"))
        self.replace_combo.lineEdit().editingFinished.connect(
            lambda: self.textfield_update_save(self.replace_combo, "replace_history"))
        self.add_text_combo.lineEdit().editingFinished.connect(
            lambda: self.textfield_update_save(self.add_text_combo, "add_text_history"))
        self.add_text_position_spinbox.valueChanged.connect(self.update_mirror_data)
        self.add_text_checkbox.stateChanged.connect(self.update_mirror_data)
        self.case_sensitive_checkbox.stateChanged.connect(self.update_mirror_data)
        self.search_replace_checkbox.stateChanged.connect(self.update_mirror_data)

        self.match_case_sensitive_checkbox.stateChanged.connect(self.update_file_data)
        self.include_subfolders_checkbox.stateChanged.connect(self.update_file_data)

        self.clear_match_button.clicked.connect(self.clear_match_string)
        self.clear_extension_button.clicked.connect(self.clear_extension_string)

        self.file_tree.setContextMenuPolicy(Qt.CustomContextMenu)
        self.file_tree.customContextMenuRequested.connect(self.open_context_menu)

        self.file_tree.itemChanged.connect(self.handle_tree_item_change)
        self.mirror_tree.itemChanged.connect(self.handle_tree_item_change)

        self.file_tree.verticalScrollBar().valueChanged.connect(self.sync_scroll)
        self.mirror_tree.verticalScrollBar().valueChanged.connect(self.sync_scroll)

        self.file_tree.itemExpanded.connect(
            lambda item: self.sync_item_expansion(item, self.mirror_tree, True))
        self.file_tree.itemCollapsed.connect(
            lambda item: self.sync_item_expansion(item, self.mirror_tree, False))
        self.mirror_tree.itemExpanded.connect(
            lambda item: self.sync_item_expansion(item, self.file_tree, True))
        self.mirror_tree.itemCollapsed.connect(
            lambda item: self.sync_item_expansion(item, self.file_tree, False))

        self.logfile_browse_button.clicked.connect(self.browse_logfile_location)

        self.match_combo.lineEdit().editingFinished.connect(self.update_mirror_data)
        self.extension_combo.lineEdit().editingFinished.connect(self.update_mirror_data)
        self.search_combo.lineEdit().editingFinished.connect(self.update_mirror_data)
        self.replace_combo.lineEdit().editingFinished.connect(self.update_mirror_data)
        self.add_text_combo.lineEdit().editingFinished.connect(self.update_mirror_data)
        self.add_text_position_spinbox.valueChanged.connect(self.update_mirror_data)

        self.rename_button.clicked.connect(self.rename_files)

    def on_combobox_activated(self, combo_box, history_key):
        if history_key in ["path_history", "match_history", "extension_history"]:
            self.update_file_data()
        else:
            self.update_mirror_data()
        self.textfield_update_save(combo_box, history_key)

    def textfield_update_save(self, combo_box, history_key):
        text = combo_box.currentText()
        if not text or not len(text):
            return
        if text in self.settings[history_key]:
            self.settings[history_key].remove(text)
        if len(self.settings[history_key]) >= 10:
            self.settings[history_key].pop(-1)
        self.settings[history_key].insert(0, text)
        self.save_settings()
        combo_box.clear()
        combo_box.addItems(self.settings[history_key])
        combo_box.setCurrentText(text)

    def browse_directory(self):
        directory = QFileDialog.getExistingDirectory(self, 'Select Directory', self.settings["path_history"][0])
        if directory:
            directory = directory.replace('/', '\\')
            if directory not in self.settings["path_history"]:
                if len(self.settings["path_history"]) >= 10:
                    self.settings["path_history"].pop(-1)
                self.settings["path_history"].insert(0, directory)
            self.save_settings()
            self.path_combo.clear()
            self.path_combo.addItems(self.settings["path_history"])
            self.path_combo.setCurrentText(directory)
            self.update_file_data()

    def clear_match_string(self):
        self.match_combo.setCurrentText("")
        self.update_file_tree()

    def clear_extension_string(self):
        self.extension_combo.setCurrentText("")
        self.update_file_data()

    def get_all_file_paths(self, directory, include_subfolders):
        all_files = []
        for root, dirs, files in os.walk(directory):
            if not include_subfolders and root != directory:
                continue
            for file in files:
                all_files.append(os.path.join(root, file))
        return all_files

    def filter_paths(self, all_files, match_string, extension_string):
        # Escape special characters in match_string
        escaped_match_string = re.escape(match_string) if match_string else ""

        # Determine the regex flags based on the case sensitivity checkbox
        flags = 0 if self.match_case_sensitive_checkbox.isChecked() else re.IGNORECASE
        match_regex = re.compile(escaped_match_string, flags) if match_string else None
        extension_set = set(extension_string.split(',')) if extension_string else None

        def filter_file(file_path):
            file_name = os.path.basename(file_path)
            match = (not match_regex or match_regex.search(file_name))
            extension = (not extension_set or file_name.split('.')[-1] in extension_set)
            return file_path if match and extension else None

        with concurrent.futures.ThreadPoolExecutor() as executor:
            filtered_files = list(filter(None, executor.map(filter_file, all_files)))

        return filtered_files

    def build_tree_structure(self, filtered_files):
        tree_structure = {}
        for file_path in filtered_files:
            root, file = os.path.split(file_path)
            if root not in tree_structure:
                tree_structure[root] = []
            tree_structure[root].append(file)
        return tree_structure

    def update_file_data(self):
        directory = self.path_combo.currentText().replace('/', '\\')
        match_string = self.match_combo.currentText()
        extension_string = self.extension_combo.currentText()
        include_subfolders = self.include_subfolders_checkbox.isChecked()

        if directory and os.path.isdir(directory):
            self.file_data = self.build_file_data(directory, match_string, extension_string, include_subfolders)
            if self.file_data:
                self.populate_tree_widget(self.file_tree, self.file_data)
                self.update_mirror_data()
            else:
                logging.error(f"No file data built for directory: {directory}")
        else:
            logging.error(f"Invalid directory: {directory}")

    def build_file_data(self, directory, match_string, extension_string, include_subfolders):
        root = TreeData(os.path.basename(directory), directory, is_file=False)

        def add_files_recursively(parent, path):
            try:
                children = []
                with os.scandir(path) as it:
                    for entry in it:
                        if entry.is_file():
                            if self.matches_criteria(entry.name, match_string, extension_string):
                                child = TreeData(entry.name, entry.path)
                                children.append(child)
                        elif entry.is_dir() and include_subfolders:
                            child = TreeData(entry.name, entry.path, is_file=False)
                            if add_files_recursively(child, entry.path) or self.matches_criteria(entry.name,
                                                                                                 match_string, ""):
                                children.append(child)

                if children or self.matches_criteria(os.path.basename(path), match_string, ""):
                    for child in children:
                        parent.add_child(child)
                    return True
                return False

            except PermissionError:
                logging.warning(f"Permission denied: {path}")
                return False

        add_files_recursively(root, directory)
        return root

    def matches_criteria(self, filename, match_string, extension_string):
        if match_string and not re.search(match_string, filename, re.IGNORECASE):
            return False
        if extension_string:
            ext = os.path.splitext(filename)[1][1:]
            if ext not in extension_string.split(','):
                return False
        return True

    def populate_tree_widget(self, tree_widget, file_data):
        tree_widget.clear()

        def build_tree_item(data):
            item = QTreeWidgetItem([data.name])
            item.setFlags(item.flags() | Qt.ItemIsUserCheckable)
            item.setCheckState(0, Qt.Checked if data.is_ticked else Qt.Unchecked)
            item.setForeground(0, QColor(data.color))
            item.setData(0, Qt.UserRole, data)

            for child in data.children:
                child_item = build_tree_item(child)
                item.addChild(child_item)

            return item

        root_item = build_tree_item(file_data)
        tree_widget.addTopLevelItem(root_item)
        tree_widget.expandAll()

    def update_mirror_data(self):
        if not self.file_data:
            return

        self.mirror_data = copy.deepcopy(self.file_data)
        self.apply_transformations(self.mirror_data)
        self.populate_tree_widget(self.mirror_tree, self.mirror_data)
        self.update_rename_button_state()

    def apply_transformations(self, data):
        search_replace = self.search_replace_checkbox.isChecked()
        search_string = re.escape(self.search_combo.currentText())
        replace_string = self.replace_combo.currentText()
        case_sensitive = self.case_sensitive_checkbox.isChecked()
        add_text = self.add_text_checkbox.isChecked()
        add_text_value = self.add_text_combo.currentText()
        add_text_position = self.add_text_position_spinbox.value()

        def transform(node):
            if node.is_file:
                new_name = node.name
                if search_replace and search_string:
                    flags = 0 if case_sensitive else re.IGNORECASE
                    new_name = re.sub(search_string, replace_string, new_name, flags=flags)
                if add_text:
                    name, ext = os.path.splitext(new_name)
                    pos = len(name) + add_text_position + 1 if add_text_position < 0 else min(add_text_position,
                                                                                              len(name))
                    new_name = name[:pos] + add_text_value + name[pos:] + ext

                if new_name != node.name:
                    node.name = new_name
                    node.full_path = os.path.join(os.path.dirname(node.full_path), new_name)
                    node.update_metadata()

            for child in node.children:
                transform(child)

        transform(data)

    def update_rename_button_state(self):
        can_rename = any(node.is_ticked and node.color == "green"
                         for node in self.get_all_nodes(self.mirror_data) if node.is_file)
        self.rename_button.setEnabled(can_rename)

    def get_all_nodes(self, node):
        yield node
        for child in node.children:
            yield from self.get_all_nodes(child)





    def rename_files(self):
        stop_on_error = self.stop_on_error_checkbox.isChecked()
        include_logfile = self.include_logfile_checkbox.isChecked()
        logfile_location = self.logfile_location_edit.text()

        renamed_files = []

        for node in self.get_all_nodes(self.mirror_data):
            if node.is_ticked and node.is_file and node.color == "green":
                old_path = node.full_path
                new_path = os.path.join(os.path.dirname(old_path), node.name)
                try:
                    os.rename(old_path, new_path)
                    renamed_files.append(f"{old_path} -> {new_path}")
                except Exception as e:
                    if stop_on_error:
                        QMessageBox.critical(self, "Error", f"Error renaming {old_path} to {new_path}: {e}")
                        return
                    elif include_logfile:
                        self.write_log(logfile_location, f'Error renaming {old_path} to {new_path}: {e}')

        renamed_files_str = "\n".join(renamed_files)
        if renamed_files:
            log_message = f"Files renamed successfully:\n{renamed_files_str}"
            if include_logfile:
                try:
                    self.write_log(logfile_location, log_message)
                except Exception as e:
                    logging.error(f"Error writing log file: {e}")

            self.show_rename_log(log_message)
        else:
            self.show_rename_log("No files renamed")
        self.update_file_data()

    def show_rename_log(self, log_message):
        dialog = QDialog(self)
        dialog.setWindowTitle("result")

        # Create the QPlainTextEdit widget
        text_edit = QPlainTextEdit()
        text_edit.setPlainText(log_message)
        text_edit.setReadOnly(True)

        # Create a custom layout and add widgets
        layout = QVBoxLayout()
        layout.addWidget(text_edit)  # Add the text edit widget

        dialog.setLayout(layout)

        # Adjust the size of the dialog to fit the content
        dialog.resize(600, 400)  # Adjust the size as needed
        dialog.exec_()

    def write_log(self, logfile_location, log_message, error=None):
        try:
            if not os.path.exists(logfile_location):
                os.makedirs(logfile_location)
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            log_filename = f"log_{timestamp}.txt"
            log_filepath = os.path.join(logfile_location, log_filename)
            with open(log_filepath, "w") as log_file:
                if error:
                    log_file.write(f"Error renaming files: {error}\n")
                log_file.write(log_message)
        except Exception as e:
            logging.error(f"Error writing log file: {e}")

    def open_context_menu(self, position):
        item = self.file_tree.itemAt(position)
        if item:
            menu = QMenu()
            open_in_explorer_action = menu.addAction("Open in Explorer")
            open_with_vlc_action = menu.addAction("Open with VLC")

            action = menu.exec_(self.file_tree.viewport().mapToGlobal(position))
            file_data = item.data(0, Qt.UserRole)
            path = file_data.full_path

            if action == open_in_explorer_action:
                if os.path.isdir(path):
                    subprocess.Popen(f'explorer "{path}"')
                else:
                    subprocess.Popen(f'explorer /select,"{path}"')
            elif action == open_with_vlc_action:
                if path.lower().endswith('.mp4'):
                    vlc_path = r"C:\Program Files\VideoLAN\VLC\vlc.exe"  # Update this path to your VLC executable
                    subprocess.Popen([vlc_path, path])
                else:
                    QMessageBox.warning(self, "Warning", "This file type is not supported by VLC.")

    def handle_tree_item_change(self, item, column):
        file_data = item.data(0, Qt.UserRole)
        if file_data is not None:
            file_data.is_ticked = item.checkState(0) == Qt.Checked
            self.update_checkstate(file_data, item)

            # Determine which tree is the 'other' tree
            source_tree = item.treeWidget()
            other_tree = self.mirror_tree if source_tree == self.file_tree else self.file_tree

            self.sync_checkstate(item, other_tree)
            self.update_rename_button_state()
        else:
            tree_name = "file" if item.treeWidget() == self.file_tree else "mirror"
            logging.error(f"No FileData associated with {tree_name} tree item: {item.text(0)}")

    def update_checkstate(self, file_data, item):
        for i in range(item.childCount()):
            child_item = item.child(i)
            child_data = child_item.data(0, Qt.UserRole)
            child_data.is_ticked = file_data.is_ticked
            child_item.setCheckState(0, item.checkState(0))
            self.update_checkstate(child_data, child_item)

    def sync_item_expansion(self, item, other_tree, expanded):
        path = []
        while item:
            path.append(item.text(0))
            item = item.parent()
        path.reverse()

        other_item = other_tree.invisibleRootItem()
        for name in path:
            for i in range(other_item.childCount()):
                if other_item.child(i).text(0) == name:
                    other_item = other_item.child(i)
                    break
        other_item.setExpanded(expanded)

    def sync_checkstate(self, changed_item, other_tree):
        path = []
        item = changed_item
        while item:
            path.append(item.text(0))
            item = item.parent()
        path.reverse()

        other_item = other_tree.invisibleRootItem()
        for name in path:
            for i in range(other_item.childCount()):
                if other_item.child(i).text(0) == name:
                    other_item = other_item.child(i)
                    break

        other_item.setCheckState(0, changed_item.checkState(0))
        file_data = other_item.data(0, Qt.UserRole)
        if file_data is not None:
            file_data.is_ticked = changed_item.checkState(0) == Qt.Checked
            self.update_checkstate(file_data, other_item)

    def browse_logfile_location(self):
        directory = QFileDialog.getExistingDirectory(self, 'Select Logfile Directory', self.settings["logfile_location"])
        if directory:
            directory = directory.replace('/', '\\')
            self.logfile_location_edit.setText(directory)

    def closeEvent(self, event):
        # Update settings
        self.settings["stop_on_error"] = self.stop_on_error_checkbox.isChecked()
        self.settings["include_logfile"] = self.include_logfile_checkbox.isChecked()
        self.settings["logfile_location"] = self.logfile_location_edit.text()
        self.settings["add_text"] = self.add_text_checkbox.isChecked()
        self.settings["add_text_value"] = self.add_text_combo.currentText()
        self.settings["search_replace_checked"] = self.search_replace_checkbox.isChecked()
        self.settings["case_sensitive_checked"] = self.case_sensitive_checkbox.isChecked()
        self.settings["include_subfolders_checked"] = self.include_subfolders_checkbox.isChecked()
        self.settings["match_case_sensitive_checked"] = self.match_case_sensitive_checkbox.isChecked()
        self.settings["add_text_checked"] = self.add_text_checkbox.isChecked()
        self.settings["logging_level"] = self.logging_level_combo.currentText()
        self.save_settings()
        event.accept()

def main():
    app = QApplication(sys.argv)
    ex = Krenamer()
    ex.show()
    sys.exit(app.exec_())

if __name__ == '__main__':
    main()
