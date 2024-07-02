import os

class TreeData:
    def __init__(self, name, full_path, is_file=True):
        self.name = name
        self.full_path = full_path
        self.is_file = is_file
        self.is_ticked = True
        self.color = "black"
        self.file_exists = os.path.exists(full_path)
        self.children = []
        self.functions = []

    def add_child(self, child):
        self.children.append(child)

    def add_function(self, func, args=None):
        self.functions.append((func, args))

    def update_metadata(self):
        self.file_exists = os.path.exists(self.full_path)
        # Update color based on conditions
        if not self.is_ticked:
            self.color = "black"
        elif self.file_exists:
            self.color = "red"
        else:
            self.color = "green"

    def apply_functions(self):
        for func, args in self.functions:
            if args:
                func(self, *args)
            else:
                func(self)


